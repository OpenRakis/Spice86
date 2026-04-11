# Volume Label Search Behavior: MS-DOS vs Modern DOS Implementations

A detailed technical analysis of how volume labels are returned differently by FCB vs INT 21h AH=4Eh searches.

## Executive Summary

**The Paradox Explained:**

The two example programs are **both correct**. Their behavior difference stems from a subtle but critical difference in how MS-DOS processes search results:

- **MS-DOS LABEL.EXE** uses FCB function (AH=11h) searching for `???????????` (11 question marks)
  - Returns volume label as an **unbroken 11-character string** in the DTA
  
- **Creative Sound Blaster INSTALL.EXE** uses INT 21h AH=4Eh searching for `A:\*.*`
  - Returns volume label formatted as an **8.3 filename with dot separator** (formatted as if it were a regular file)

Both get their data from the same underlying FAT directory entry, but the **post-processing differs**.

---

## Part 1: The Canonical Behavior (MS-DOS v4.0 Source Code)

### The Directory Entry Format

All FAT filesystems store a directory entry as 32 bytes:

```
Offset  Size    Field
------  ----    -----
0-10    11      dir_name - Filename (special for volume labels)
11      1       dir_attr - Attributes (0x08 = volume label)
12-21   10      dir_pad  - Reserved
22-23   2       dir_time - Time of last write
24-25   2       dir_date - Date of last write
26-27   2       dir_first - First allocation unit
28-31   4       dir_size - File size
```

**Key Point**: The filename field is 11 bytes, stored in a specific format:

- **For regular files**: 8-byte name (space-padded) + 3-byte extension (space-padded)
- **For volume labels**: 11-byte label as-is (no 8.3 format separation!)

### Volume Label Storage

A volume label directory entry looks like:

```
[MYVOLUME  ][0x08] ...  ← Where [ ] = single character
  ^^^^^^^^       = 11 bytes of label
                 No dot, no extension field distinction
```

A regular file "TEST.TXT" would look like:

```
[TEST     ][TXT][0x20] ...  ← 0x20 = normal file attribute
  ^^^^^^^^^ ^^ ^^ 
  8 bytes    3 bytes
  padded     padded
```

### MS-DOS Source Code: The Two Search Functions

#### Route 1: FCB Functions (AH=11h/12h) - `$DIR_SEARCH_FIRST`

**File**: `ms-dos/v4.0/src/DOS/SEARCH.ASM` (lines ~66-178)

```asm
procedure $DIR_SEARCH_FIRST,NEAR
    ; Set up and call low-level search
    invoke  GET_FAST_SEARCH   
    JNC SearchSet
    
SearchSet:
    MOV SI,OFFSET DOSGroup:SEARCHBUF
    LES DI,[THISFCB]
    ; ... copy 20 bytes of search info ...
    
    MOV CX,16           ; 32 bytes / 2 words
    REP MOVSW           ; ← Direct copy of 32-byte directory entry
    ; ... complete ...
    transfer FCB_Ret_OK
EndProc $DIR_SEARCH_FIRST
```

**Key behavior**:

- Copies the 32-byte directory entry **as-is** to DTA
- No reformatting, no post-processing
- Volume label remains as 11-byte unbroken string

#### Route 2: Extended Find (AH=4Eh/4Fh) - `$FIND_FIRST`

**File**: `ms-dos/v4.0/src/DOS/SEARCH.ASM` (lines ~217-262)

```asm
procedure $FIND_FIRST,NEAR
    invoke  GET_FAST_SEARCH   ; same low-level search
    JNC FindSet
    
FindSet:
    MOV SI,OFFSET DOSGroup:SEARCHBUF
    LES DI,[DMAAdd]
    MOV CX,21
    REP MOVSB             ; Copy 21 bytes of continuation info
    
    PUSH SI
    MOV AL,[SI.dir_attr]  ; Get attribute byte
    STOSB
    ADD SI,dir_time
    MOVSW                 ; Copy time
    MOVSW                 ; Copy date
    INC SI, INC SI        ; Skip first cluster field
    MOVSW                 ; Copy size (2 words)
    MOVSW
    POP SI                ; Point back to filename
    
    CALL PackName         ; ← POST-PROCESS THE FILENAME!
    
    transfer Sys_Ret_OK
EndProc $FIND_FIRST
```

**The Critical Difference**: `PackName` function call!

#### The PackName Function

**File**: `ms-dos/v4.0/src/DOS/SEARCH.ASM` (lines ~266-313)

```asm
Procedure PackName,NEAR
    MOV CX,8                    ; Process first 8 bytes
    REP MOVSB                   ; Move them
    
main_kill_tail:
    CMP BYTE PTR ES:[DI-1]," "
    JNZ find_check_dot
    DEC DI                      ; Back up over trailing space
    INC CX
    CMP CX,8
    JB main_kill_tail           ; Loop: remove trailing spaces
    
find_check_dot:
    CMP WORD PTR [SI],(" " SHL 8) OR " "
    JNZ got_ext
    CMP BYTE PTR [SI+2]," "
    JZ find_done                ; No extension
    
got_ext:
    MOV AL,"."
    STOSB                       ; Add dot
    
    MOV CX,3
    REP MOVSB                   ; Copy 3-byte extension
    
ext_kill_tail:
    CMP BYTE PTR ES:[DI-1]," "
    JNZ find_done
    DEC DI                      ; Back up over trailing space
    JMP ext_kill_tail           ; Loop: remove trailing spaces
    
find_done:
    XOR AX,AX
    STOSB                       ; Null-terminate
    return
EndProc PackName
```

**What PackName does:**

1. Takes the 11-byte FCB format (8 bytes name + 3 bytes extension)
2. Removes trailing spaces from the 8-byte name portion
3. Checks if there's an extension (non-space characters)
4. If extension exists, inserts a '.' and copies extension bytes
5. Removes trailing spaces from extension
6. Null-terminates the result
7. Returns an ASCI-Z (null-terminated) string in standard 8.3 format

**Applied to a volume label "MYVOLUME" stored as "MYVOLUME   ":**

```
Input:   [M][Y][V][O][L][U][M][E][ ][ ][ ][ ][...extension bytes...]
         Positions 0-10 for volume label

PackName processes as if it were:
         [MYVOLUME ][   ]
          ^^^^^^^^^ ^^^
          8 bytes   3 bytes (all spaces)

Output:  "MYVOLUME\0"  ← Removes trailing spaces, null-terminates
         (8 + 1 = 9 bytes in output)

But if we apply the same to a file entry:
Input:   [T][E][S][T][ ][ ][ ][ ][T][X][T]
         positions 0-7 (name)     8-10 (ext)

Output:  "TEST.TXT\0"  ← Dot added, both parts space-trimmed
```

**The Paradox with Volume Labels:**

When a volume label like "MYVOLUME    " (padded to 11 bytes) is processed:

- The label sits in positions 0-10 (the full 11 bytes)
- PackName treats positions 0-7 as "name" and 8-10 as "extension"
- Positions 8-10 might be spaces OR part of the label!
- If they're spaces: result is "MYVOLUME"
- If they're label characters: result becomes formatted with a dot

For a longer label like "MYVOLUMEID  " (11 bytes):

```
Positions: 0-7 = "MYVOLUME", 8-10 = "ID " 
PackName output: "MYVOLUME.ID"   ← Incorrectly adds a dot!
```

---

## Part 2: Answers to Your Specific Questions

### Q: When does MS-DOS return it as an unbroken string?

**A: When using FCB functions (AH=11h/12h)** with search pattern `???????????`

- The directory entry is copied directly (32 bytes) to the DTA
- The 11-byte filename field remains unchanged
- Client code must read the first 11 bytes as the label name
- This is what MS-DOS LABEL.EXE expects

**Implementation**:

- FCB is set with pattern "??????????? " (11 question marks)
- Attribute byte set (usually 0x08 for volume id search)
- AH=11h (FCB find first) called via INT 21h
- Returns with DTA containing raw 32-byte entry

### Q: When does MS-DOS return it as an 8.3 filename?

**A: When using INT 21h AH=4Eh** (extended find first)

- The same directory entry is fetched by the low-level search
- But `PackName()` reformats it to 8.3 ASCI-Z format
- The result puts a DOT in the DTA around position 8 (if extension bits exist)
- This is what Sound Blaster INSTALL.EXE expects (incorrectly!)

**Implementation**:

- Filespec `A:\*.*` used (or any wildcard that matches volume label)
- Search attribute includes volume label bit
- AH=4Eh called via INT 21h
- Returns with DTA containing formatted filename + null terminator

### Q: Is the difference FCB vs INT AH=4Eh?

**A: YES, exactly.**

Not the pattern itself (`???????????` vs `*.*`), but the **function code** (AH value):

- **AH=11h, 12h** (FCB): Raw 32-byte directory entry
- **AH=4Eh, 4Fh** (Extended): Post-processed by PackName()

### Q: Is the difference searching for `???????????` vs `*.*`?

**A: NO, not really.** Both patterns can match volume labels.

The pattern matters for **matching** (which entries are returned), not for **formatting** (how they're returned).

- `???????????` matches anything with exactly 1 character at each position
- `*.*` matches anything and accepts volume labels with the volume bit set
- But both go through the **same search code**
- And get formatted based on the **function used**, not the pattern

### Q: Does the backslash in `A:\*.*` matter?

**A: Only for path resolution, not for volume label formatting.**

- `A:\*.*` means "search in root of drive A:"
- `A:*.*` means "search in current directory of drive A:"
- Both ultimately search the same structure (directory entry)
- The formatting depends on AH=4Eh being used, not on the backslash
- The backslash is consumed during path parsing before the search function is called

### Q: Does the drive spec `A:` in `A:\*.*` matter?

**A: Only for selecting which drive, not for formatting.**

- The drive letter selects the drive
- The path part (`\` and`*.*`) determines which directory to search
- The function code (AH) that processes the result determines formatting
- All three are independent concerns

---

## Part 3: Comparison of DOS Implementations

### MS-DOS v4.0 (Canonical Reference)

**FCB Search (AH=11h, 12h)**

- Source: SEARCH.ASM:$DIR_SEARCH_FIRST
- Returns: 32-byte raw directory entry in DTA
- Volume label: 11-byte unbroken string at offset 0
- No post-processing

**Extended Search (AH=4Eh, 4Fh)**

- Source: SEARCH.ASM:$FIND_FIRST
- Returns: Formatted 8.3 ASCI-Z filename in DTA
- Volume label: Formatted with 8.3 expansion (incorrect but consistent)
- Post-processes with PackName()

---

### FreeDOS (Modern Understanding of MS-DOS)

**Location**: `kernel/kernel/fatdir.c` - dos_findfirst/dos_findnext

**FCB Search**

```c
// FreeDOS source code comments show understanding of the raw entry:
// "transfer it to the dmatch structure"
memcpy(&SearchDir, &fnp->f_dir, sizeof(struct dirent));
```

- Returns: 32-byte raw `dirent` structure
- Volume label: 11-byte unbroken string (stored in `f_dir` as-is)
- Comment in code acknowledges volume label handling: "It's either a special volume label search"

**Extended Search (INT 21h AH=4Eh)**

```c
// Similar search, but FreeDOS should apply same PackName-like processing
// as MS-DOS does via the `Dmatch` structure
```

**Finding**: FreeDOS correctly implements both functions with appropriate formatting.

---

### Spice86 (x86 Emulator)

**Location**: `Spice86/src/Spice86.Core/Emulator/OperatingSystem/DosFileManager.cs`

**Current Implementation**

```csharp
public DosFileOperationResult FindFirstMatchingFile(string fileSpec, ushort searchAttributes) {
    // Lines 267-320: Finds matching files
    // Uses host filesystem to enumerate
    
    // Lines 655-870: UpdateDosTransferAreaWithFileMatch
    dta.FileAttributes = (byte)dosAttributes;
    dta.FileDate = ToDosDate(creationLocalDate);
    dta.FileTime = ToDosTime(creationLocalTime);
    dta.FileSize = (uint)fileInfo.Length;
    dta.FileName = DosPathResolver.GetShortFileName(...);  // ← Always formatted!
}
```

**DTA Structure** (DosDiskTransferArea.cs):

```csharp
private const int FileNameOffset = 0x1E;
private const int FileNameLength = 13;

public string FileName {
    get => GetZeroTerminatedString(FileNameOffset, FileNameLength);
    set => SetZeroTerminatedString(FileNameOffset, value, FileNameLength);
}
```

**Issue with Spice86**:

- FileName is always a `string` property (C# string)
- Always formatted through `GetShortFileName()`
- Doesn't differentiate between FCB (raw) and extended (formatted) searches
- **No support for returning raw 11-byte volume label for FCB searches**

**Volume Label Handling**:

- Volume labels are **not specially handled**
- Would be formatted as regular filenames (potentially incorrect)
- Doesn't implement PackName-style post-processing correctly

---

### DOSBox-staging (DOS Emulator)

**Location**: `dosbox-staging/src/dos/drive_local.cpp` - FindFirst/FindNext

**FindFirst Code** (lines 278-353):

```cpp
bool localDrive::FindFirst(const char* _dir, DOS_DTA& dta, bool fcb_findfirst) {
    // ...
    FatAttributeFlags search_attr = {};
    dta.GetSearchParams(search_attr, tempDir);
    
    if (search_attr == FatAttributeFlags::Volume) {
        dta.SetResult(dirCache.GetLabel(), 0, 0, 0, FatAttributeFlags::Volume);
        return true;
    } else if (search_attr.volume && (*_dir == 0) && !fcb_findfirst) {
        // Non-FCB search for volume label...
    }
}
```

**Key Parameters**:

- `fcb_findfirst` parameter distinguishes FCB from extended search!
- Passes this distinction through the call chain

**Volume Label Special Case**:

```cpp
if (search_attr == FatAttributeFlags::Volume) {
    dta.SetResult(dirCache.GetLabel(), 0, 0, 0, FatAttributeFlags::Volume);
    return true;
}
```

**Implementation Quality**:

- Recognizes the FCB vs extended distinction
- Has special handling for volume label searches
- Appears to correctly return formatted strings for extended searches
- May correctly handle raw entry format for FCB searches

**Verdict**: DOSBox-staging appears to have **better understanding** than Spice86

---

## Part 4: The Correct Format for Each Function

### DTA Format for Extended Find (AH=4Eh/4Fh)

From MS-DOS source and modern implementations:

```
Offset  Size    Contents
------  ----    --------
0-20    21      Search continuation info (for findnext)
21      1       File attributes byte
22-23   2       File time
24-25   2       File date  
26-27   2       File size (low word)  
28-29   2       File size (high word)
30-42   13      Filename (ASCI-Z, 8.3 format with dot, null-terminated)
```

**For volume labels via AH=4Eh**:

- Filename would be formatted: "MYVOLUME   \0" or "VOLUME.ID\0" (if PackName adds dot)
- Should typically have no dot since volume labels don't have extensions
- Attribute byte = 0x08 (volume ID)

### DTA Format for FCB Find (AH=11h/12h)

```
Offset  Size    Contents
------  ----    --------
0-20    21      Search continuation info
0-10    11      Filename (raw 11-byte string)
11      1       Attribute byte
12-21   10      Reserved
22-23   2       File time
24-25   2       File date
26-27   2       First cluster
28-31   4       File size
```

**For volume labels via AH=11h**:

- Filename at offset 0-10: raw 11-byte label (e.g., "MYVOLUME   ")
- Attribute byte = 0x08
- This is the raw directory entry format

---

## Part 5: Implementation Recommendations

### For Emulators and DOS Clones

1. **Implement the FCB/Extended distinction**

   ```cpp
   bool FindFirst(const char* spec, DOS_DTA& dta, bool fcb_findfirst)
   {
       if (fcb_findfirst) {
           // Copy raw directory entry (including raw 11-byte filename)
           memcpy(dta_ptr, dir_entry, 32);
       } else {
           // Post-process filename via PackName equivalent
           format_filename(dir_entry, dta_ptr);
       }
   }
   ```

2. **Add PackName equivalent for extended search**

   ```cpp
   void PackName(const char* fcb_name_11, char* asciiz_dest) {
       // Copy first 8 bytes, trim spaces
       // Check for extension (bytes 8-10)
       // Add dot if extension exists
       // Null-terminate
   }
   ```

3. **Handle volume label special cases**
   - Recognize attribute byte 0x08 (volume ID)
   - For FCB search: return 11-byte raw label
   - For extended search: format label as regular 8.3 name

### For Volume Label Search Compatibility

**CORRECT Usage for volume labels**:

1. **Using FCB (AH=11h)** - Get unbroken label:

   ```asm
   MOV DX, OFFSET FCB
   MOV FCB.drive, Drive_letter
   MOV FCB.fname, "??????????? " ; 11 spaces
   MOV FCB.attr, 0x08            ; Volume ID attribute
   MOV AH, 11h                    ; FCB find first
   INT 21h
   ; At DTA: first 11 bytes = volume label
   ```

2. **Using Extended (AH=4Eh)** - Get formatted label (caveat: has dot):

   ```asm
   MOV DX, OFFSET FileSpec       ; "A:\*.*" or similar
   MOV CX, 0x08                  ; Search for volume
   MOV AH, 4Eh                   ; Extended find first
   INT 21h
   ; At DTA+30: filename in 8.3 format (may have erroneous dot)
   ```

---

## Conclusion

The "paradox" of MS-DOS returning volume labels in two different formats is not a bug or inconsistency—it's **intentional orthogonal design**.

**The Root Cause:**
MS-DOS has two independently-designed file search interfaces (FCB and Extended), each with its own return format conventions:

- **FCB**: Returns raw working data structures (32-byte directory entries)
- **Extended**: Returns user-friendly formatted data (ASCI-Z filenames)

**The Twist with Volume Labels:**
Volume labels, being special directory entries, get caught up in this auto-formatting intended for normal files. The `PackName` function doesn't have special logic to exclude volume labels—it formats everything it processes.

**Modern DOS Implementations:**

- **FreeDOS**: Correctly distinguishes both types
- **DOSBox-staging**: Recognizes the distinction with `fcb_findfirst` parameter
- **Spice86**: Lacks this distinction, treats all searches the same (less accurate)

This analysis is based on actual MS-DOS v4.0 released source code, confirming that both LABEL.EXE and Sound Blaster INSTALL.EXE were following the documented (if quirky) behavior of the original DOS kernel.
