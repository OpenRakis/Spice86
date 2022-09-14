This folder contains various ghidra scripts that can be used to import spice86 data dumps into ghidra and generate code from there.

For now the scripts use data extracted from spice86 run. They search the data in the folder defined in environment variable SPICE86_DUMPS_FOLDER

Here is a list of what they do:
 - Spice86ReferenceGenerator: This script reads the Execution flow json file dumped by the emulator and overwrites ghidra guesses for jumps / calls with the real references observed by the emulator.
 - Spice86UnalignedReturnsDetector: This script lists a part of the labels corresponding to function returns that will cause the generated code to crash. If you see some, create a function there. 
 - Spice86TentativeFunctionRenamer: This script renames functions created in ghidra (named FUN_...) to a name with a tentative real mode segment offset like the one output by spice86. You need to specify the segments observed by the emulator in the segments variable for now.
 - Spice86InstructionDeleter: Deletes instructions in specified range (hardcoded). This is useful when replacing bytes in ghidra as it resets the disassembly.
 - Spice86FunctionCreator: Creates functions at addresses given in hardcoded list addresses. Use this when the generator says there is no function at address xxx.
 - Spice86FunctionSanitizer: Some function calls are sometimes done with jumps instead of calls in assembly. Ghidra will probably consider that the long jump target is in the same function as the source of the jump. This script splits functions with disjoint bodies into several functions so that generated code is easier to read.
 - Spice86CodeGenerator: Generates code from ghidra ready to be imported in spice86. The logs are output in text file ghidrascriptout.txt. Please check that there are no errors there as any error means the code will likely not work. Errors are usually fixable by tuning ghidra listing, either by fixing errors in ghidra or by recreating functions there. Other scripts listed above can help to do so.
