namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.Dos;
using Spice86.Tests.Dos.FileSystem;

using System.Text;

using static Spice86.Tests.AsmFixtures.DosProgramRun;
using static Spice86.Tests.Dos.BatchTestHelpers;

/// <summary>
/// Scenarios of the DOS batch routing programs and of the AUTOEXEC bootstrap routing. Every scenario
/// builds its own COM helpers, batch scripts and data files inside a fresh per-run directory mounted as
/// C:, so the discovery run and the generated execution start from identical host files, redirected
/// input and preloaded keys, and a script that renames, deletes or redirects never pollutes another run.
/// Two different scripts both named START.BAT never share a stable identity, so they never share a
/// golden key. Every scenario runs the same entry file, the same script and the same oracle in the
/// interpreted and in the generated mode.
/// </summary>
internal static class DosBatchScenarioCatalog {
    private const string TestfkeyResourceDirectory = "DosBatchTests";
    private const string TestfkeyResourceFile = "TESTFKEY.COM";

    private static readonly IReadOnlyList<string> TestfkeyDependency =
        [$"{TestfkeyResourceDirectory}/{TestfkeyResourceFile}"];

    private const string AlphaWavesScript =
        "@echo off\r\n" +
        ":START\r\n" +
        "testfkey\r\n" +
        "if ERRORLEVEL = 10 goto FIN\r\n" +
        "if ERRORLEVEL = 4 goto START\r\n" +
        "if ERRORLEVEL = 3 goto F3\r\n" +
        "if ERRORLEVEL = 2 goto F2\r\n" +
        "if ERRORLEVEL = 1 goto F1\r\n" +
        "goto START\r\n" +
        ":F3\r\n" +
        "ALPHA_D\r\n" +
        "goto FIN\r\n" +
        ":F2\r\n" +
        "ALPHA_E\r\n" +
        "goto FIN\r\n" +
        ":F1\r\n" +
        "ALPHA_F\r\n" +
        "goto FIN\r\n" +
        ":FIN\r\n" +
        "ALPHA_X\r\n" +
        "exit\r\n";

    private const string JillOfTheJungleScript =
        "ECHO Menu\r\n" +
        "choice /c1234 /s Which game do you want to run? [1-4]: /n < KEY.TXT\r\n" +
        "if errorlevel 4 goto exit\r\n" +
        "if errorlevel 3 goto 301\r\n" +
        "if errorlevel 2 goto 201\r\n" +
        "if errorlevel 1 goto 101\r\n" +
        "\r\n" +
        ":101\r\n" +
        "@ECHO off\r\n" +
        "cls\r\n" +
        "JILL1.EXE\r\n" +
        "exit\r\n" +
        "\r\n" +
        ":201\r\n" +
        "@ECHO off\r\n" +
        "cls\r\n" +
        "JILL2.EXE\r\n" +
        "exit\r\n" +
        "\r\n" +
        ":301\r\n" +
        "@ECHO off\r\n" +
        "cls\r\n" +
        "JILL3.EXE\r\n" +
        "exit\r\n" +
        "\r\n" +
        ":exit\r\n" +
        "exit\r\n";

    private const string JillMenuScript =
        "@ECHO OFF\r\n" +
        "ECHO  1. Start Game\r\n" +
        "ECHO  2. Read Story\r\n" +
        "ECHO  3. Ordering Info\r\n" +
        "ECHO  4. Quit\r\n" +
        "CHOICE /C:1234 /N < KEY.TXT\r\n" +
        "IF ERRORLEVEL 4 GOTO QUIT\r\n" +
        "IF ERRORLEVEL 3 GOTO ORDER\r\n" +
        "IF ERRORLEVEL 2 GOTO STORY\r\n" +
        "IF ERRORLEVEL 1 GOTO GAME\r\n" +
        ":GAME\r\n" +
        "CALL GAME.COM\r\n" +
        "GOTO END\r\n" +
        ":STORY\r\n" +
        "CALL STORY.COM\r\n" +
        "GOTO END\r\n" +
        ":ORDER\r\n" +
        "CALL ORDER.COM\r\n" +
        "GOTO END\r\n" +
        ":QUIT\r\n" +
        "CALL EXIT.COM\r\n" +
        ":END\r\n";

    /// <summary>Every batch routing scenario, in the order of the routing behaviour it exercises.</summary>
    public static IReadOnlyList<AsmScenario> BatchScenarios => [
        HostRequestedComRunsThroughBatchRouting(),
        CallLaunchesComProgram(),
        CallWithoutExtensionPrefersComOverBat(),
        CallWithoutExtensionFallsBackToCom(),
        CallWithoutExtensionFallsBackToExe(),
        MaupitiTatouRunsFromNestedDirectory(),
        AitdTatouWithoutExtensionPrefersCom(),
        NestedBatchCall(),
        ImgMountInternalCommandPrecedence(),
        GotoSkipsCommands(),
        DriveChangeToZUsesMemoryDrive(),
        IfErrorLevelDispatchesCommand(),
        IfErrorLevelEqualsSyntaxDispatchesCommand(),
        AlphaWavesRoutesFunctionKey("batch_alpha_waves_f1", 0x3B00, 'F'),
        AlphaWavesRoutesFunctionKey("batch_alpha_waves_f2", 0x3C00, 'E'),
        AlphaWavesRoutesFunctionKey("batch_alpha_waves_f3", 0x3D00, 'D'),
        AlphaWavesF10BypassesBranches(),
        EchoPreservesLeadingSpaces(),
        IfNotErrorLevelSkipsCommand(),
        IfNotStringComparisonExecutesExpectedBranch(),
        IfExistAndIfNotExistUseCorrectBranch(),
        ShiftUpdatesArguments(),
        ForIteratesAndCallsBatch(),
        ForSupportsCommaAndSemicolonDelimiters(),
        ForSupportsEqualsDelimiter(),
        OutputRedirectionWritesToFile(),
        EchoSpacing("batch_echo_spacing_redirect_tight", "ECHO TEST>OUT.TXT", "TEST\r\n"),
        EchoSpacing("batch_echo_spacing_space_before_gt", "ECHO TEST >OUT.TXT", "TEST \r\n"),
        EchoSpacing("batch_echo_spacing_space_after_gt", "ECHO TEST> OUT.TXT", "TEST\r\n"),
        EchoSpacing("batch_echo_spacing_spaces_both_sides", "ECHO TEST > OUT.TXT", "TEST \r\n"),
        EchoSpacing("batch_echo_spacing_trailing_tight", "ECHO TEST>OUT.TXT  ", "TEST  \r\n"),
        EchoSpacing("batch_echo_spacing_trailing_spaced", "ECHO TEST > OUT.TXT ", "TEST  \r\n"),
        AppendRedirectionAppendsToFile(),
        PipeTransfersStdoutToStdin("batch_pipe_spaces_around_bar", "CALL PROD.COM | CALL CONS.COM"),
        PipeTransfersStdoutToStdin("batch_pipe_tight_bar", "CALL PROD.COM|CALL CONS.COM"),
        PipeTransfersStdoutToStdin("batch_pipe_space_after_bar", "CALL PROD.COM| CALL CONS.COM"),
        PipelineSupportsInputRedirectionOnFirstSegment(),
        InputRedirectionFeedsStdIn(),
        SetUpdatesDosEnvironmentForExpansion(),
        SetEmptyAssignmentClearsVariable(),
        EchoWritesToRedirectedFile(),
        InvalidRedirectionDoesNotExecuteCommand(),
        InvalidRedirectionCreatesNoOutput("batch_invalid_redirect_double_gt", "ECHO BAD > > OUT.TXT"),
        InvalidRedirectionCreatesNoOutput("batch_invalid_redirect_gt_lt_tight", "ECHO BAD >< OUT.TXT"),
        InvalidRedirectionCreatesNoOutput("batch_invalid_redirect_double_lt", "ECHO BAD < < IN.TXT"),
        InvalidRedirectionCreatesNoOutput("batch_invalid_redirect_double_bar", "ECHO BAD || MORE"),
        InvalidRedirectionCreatesNoOutput("batch_invalid_redirect_gt_bar", "ECHO BAD > | OUT.TXT"),
        InvalidRedirectionCreatesNoOutput("batch_invalid_redirect_lt_bar_tight", "ECHO BAD <| IN.TXT"),
        InvalidRedirectionCreatesNoOutput("batch_invalid_redirect_bar_gt", "ECHO BAD | > OUT.TXT"),
        MultipleOutputRedirectionsLastOneWins("batch_multi_output_tight", "ECHO VALUE>OUT1.TXT>OUT2.TXT", "VALUE\r\n"),
        MultipleOutputRedirectionsLastOneWins("batch_multi_output_padded_targets",
            "ECHO VALUE>    OUT1.TXT>     OUT2.TXT", "VALUE\r\n"),
        MultipleOutputRedirectionsLastOneWins("batch_multi_output_spaces_before_gt",
            "ECHO VALUE>OUT1.TXT  >OUT2.TXT", "VALUE  \r\n"),
        MultipleInputRedirectionsLastOneWins("batch_multi_input_spaced", "CALL READ.COM < IN1.TXT < IN2.TXT"),
        MultipleInputRedirectionsLastOneWins("batch_multi_input_tight", "CALL READ.COM<IN1.TXT<IN2.TXT"),
        DoubleInputOperatorLastOneWins(),
        InvalidRedirectionLaunchesNoCommand("batch_invalid_redirect_launch_bar_gt", "CALL WRITE.COM | > OUT.TXT"),
        InvalidRedirectionLaunchesNoCommand("batch_invalid_redirect_launch_tight_bar_gt", "CALL WRITE.COM|>OUT.TXT"),
        InvalidRedirectionLaunchesNoCommand("batch_invalid_redirect_launch_bar_lt", "CALL WRITE.COM |< IN.TXT"),
        InvalidRedirectionLaunchesNoCommand("batch_invalid_redirect_launch_bar_space_lt", "CALL WRITE.COM| < IN.TXT"),
        InvalidRedirectionLaunchesNoCommand("batch_invalid_redirect_launch_lt_gt", "CALL WRITE.COM < > IN.TXT"),
        EchoOffSetsStatusOutput(),
        EchoOnAfterOffRestoresStatusOutput(),
        EchoOnByDefaultEchoesEachLine(),
        AtPrefixSuppressesEchoForSingleLine(),
        EchoOffSuppressesSubsequentLineEchoes(),
        EchoDotSeparator("batch_echo_dot_blank", "ECHO. > OUT.TXT", " \r\n"),
        EchoDotSeparator("batch_echo_dot_text", "ECHO.HELLO > OUT.TXT", "HELLO \r\n"),
        EchoDotSeparator("batch_echo_dot_leading_spaces", "ECHO.  HELLO > OUT.TXT", "  HELLO \r\n"),
        IfUnquotedComparisonExecutesCommand(),
        ForMalformedSyntaxSkips("batch_for_malformed_single_percent", "FOR %C IN (ONE TWO) ECHO %C"),
        ForMalformedSyntaxSkips("batch_for_malformed_no_in", "FOR %C (ONE TWO) DO ECHO %C"),
        ForMalformedSyntaxSkips("batch_for_malformed_no_variable", "FOR IN (ONE TWO) DO ECHO %C"),
        ForMalformedSyntaxSkips("batch_for_malformed_no_parentheses", "FOR %C IN ONE TWO DO ECHO %C"),
        ForMalformedSyntaxSkips("batch_for_malformed_no_body", "FOR %C IN (ONE TWO) DO"),
        SetWithoutArgumentsEnumeratesEnvironment(),
        ErrorlevelPropagatesFromCalledProgram(),
        CallNestedBatchPreservesContext(),
        CallLabelInvokesInternalSubroutine(),
        CallLabelNestedSubroutines(),
        CallLabelCallsBatchFile(),
        CallLabelUnknownLabelReturnsToNextLine(),
        ForWithWildcardExpandsMatchingFiles(),
        ForWithWildcardTypeRedirect(),
        PathCommandSetsEnvironmentForExpansion(),
        PauseContinuesWithRedirectedStdin(),
        ForWildcardWithoutMatchesSkips(),
        ForMixesLiteralsAndWildcards(),
        VariableExpansionAcrossBatchCalls(),
        ClsClearsScreenAndContinues(),
        ChoiceDefaultYnSelectsY(),
        ChoiceDefaultYnSelectsN(),
        ChoiceCustomKeysSelectsThirdOption(),
        ChoiceIsCaseInsensitiveByDefault(),
        ChoiceWithSFlagIsCaseSensitive(),
        ChoiceWithoutChoiceArgumentDefaultsToYN(),
        ChoiceWithNFlagWritesNoPrompt(),
        ChoiceWithPromptTextWritesPrompt(),
        ChoiceTimeoutSelectsDefaultOnEof(),
        ChoiceTimeoutKeypressTakesPrecedence(),
        JillMenuRoutesViaErrorlevelGotoChain(),
        TypeDisplaysFileContents(),
        TypeMissingFileWritesError(),
        CdWithoutArgsPrintsCurrentDirectory(),
        CdChangesDirectory(),
        CdDotDotNavigatesToParent(),
        CdDotStaysInCurrentDirectory(),
        CdBackslashNavigatesToRoot(),
        IfStringComparisonIsCaseSensitive(),
        IfStringComparisonPreservesQuotes(),
        GotoMissingLabelStopsBatch(),
        TypeStopsAtCtrlZEof(),
        ChoiceSkipsInvalidKeys(),
        EchoOffInInnerBatchDoesNotAffectOuter(),
        BatchFileReadingStopsAtCtrlZ(),
        ExitStopsExecution(),
        ExitInsideCallStopsAllBatches(),
        BareBatInvocationReplacesCurrentBatch(),
        BareMissingBatKeepsCurrentContext(),
        ForListParsesQuotedRightParenthesis(),
        LhPassesThrough(),
        LoadhighPassesThrough(),
        MdCreatesDirectory(),
        MkdirIsAliasForMd(),
        MdOnExistingDirectoryPrintsError(),
        RdRemovesEmptyDirectory(),
        RmdirIsAliasForRd(),
        RdOnMissingDirectoryPrintsError(),
        DelRemovesSingleFile(),
        EraseIsAliasForDel(),
        DelWildcardRemovesMatchingFiles(),
        DelOnMissingFilePrintsError(),
        DelSkipsDirectories(),
        DelSkipsReadonlyFiles(),
        RenRenamesSingleFile(),
        RenameIsAliasForRen(),
        RenOnMissingFilePrintsError(),
        RenWithoutTargetPrintsError(),
        RenWildcardSourceAndTarget(),
        RenWildcardWithoutMatches(),
        VolWithDriveArgument(),
        ForWildcardExpansionDeletesMatchingFiles(),
        ForWildcardWithoutTypeMatchesCreatesNoOutput(),
        DirListsFiles(),
        DirBareFormat(),
        DirWithPattern(),
        CopySingleFile(),
        CopyWildcard(),
        CopySwitchesAreAccepted(),
        CopyOfMissingSource(),
        CopySingleFileToDirectory(),
        DatePrintsDate(),
        DateSetValueChangesDisplayedDate(),
        DateHostSyncProducesDateOutput(),
        TimePrintsTime(),
        TimeSetValueChangesDisplayedTime(),
        TimeHostSyncProducesTimeOutput(),
        VerPrintsVersion(),
        VolPrintsVolumeLabel(),
        VolOnMountedCdDrivePrintsMediaLabel(),
        DateHelpSwitchPrintsUsage(),
        TimeHelpSwitchPrintsUsage(),
        DirAdListsDirectoriesOnly(),
        DirAMinusDListsFilesOnly(),
        DirOnSortsByNameAscending(),
        DirOMinusNSortsByNameDescending(),
        SetPromptOptionPrintsUnsupportedMessage(),
        ForBareStarMatchesAllFiles(),
        PathSearchFindsExternalBat(),
        MoveRenamesFileInSameDirectory(),
        MoveFileIntoSubdirectory(),
        MoveWildcardIntoDirectory(),
        MoveOnMissingSourceContinues(),
        CopyConcatenatesTwoFiles(),
        CopyConcatenatesThreeFiles(),
        RedirectCreatesFileInCaseMismatchedSubdirectory(),
        DeleteIsAliasForDel(),
        JillOfTheJungleChoiceDispatchesToExeStub(),
        ChoiceWithoutColonParsesChoiceKeys(),
        ChoiceNFlagAfterTextSuppressesPrompt(),
        ChoiceWithPreloadedKeySetsErrorlevel(),
        PauseWithPreloadedKeyContinues()
    ];

    /// <summary>The AUTOEXEC bootstrap scenarios: every host-requested program is routed through the
    /// generated Z:\AUTOEXEC.BAT, so the bootstrap must quote, normalize and forward it unchanged.</summary>
    public static IReadOnlyList<AsmScenario> BootstrapScenarios => [
        HostRequestedExeExecutesThroughAutoexec(),
        HostRequestedExeInSubdirectoryExecutesThroughAutoexec(),
        HostRequestedProgramWithSpacesInPathIsQuoted(),
        HostRequestedBatchUsesCallToRoute(),
        HostRequestedBatchWithSlashPathNormalizesCallPath(),
        HostArgumentsArePreservedThroughAutoexec(),
        HostErrorlevelPropagatesThroughAutoexecChain()
    ];

    /// <summary>A COM program the host requested directly, executed through the same batch routing pipeline
    /// as a batch file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostRequestedComRunsThroughBatchRouting() {
        return Scenario("batch_host_requested_com", run => run
            .Binary("WRITER.COM", BuildVideoWriterCom('C', 0))
            .Run("WRITER.COM", result => Cell(result, 0).Should().Be('C')));
    }

    /// <summary>A batch file that CALLs a COM program: the batch engine must return to the script after
    /// the child terminated.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallLaunchesComProgram() {
        return Scenario("batch_call_launches_com", run => run
            .Binary("WRITER.COM", BuildVideoWriterCom('A', 0))
            .StartBatch("CALL WRITER.COM\r\n",
                result => Cell(result, 0).Should().Be('A')));
    }

    /// <summary>CALL without extension prefers TOOL.COM over a TOOL.BAT of the same base name.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallWithoutExtensionPrefersComOverBat() {
        return Scenario("batch_call_noext_prefers_com_over_bat", run => run
            .Text("TOOL.BAT", "CALL BATWIN.COM\r\n")
            .Binary("TOOL.COM", BuildVideoWriterCom('C', 0))
            .Binary("BATWIN.COM", BuildVideoWriterCom('B', 0))
            .StartBatch("CALL TOOL\r\n",
                result => Cell(result, 0).Should().Be('C')));
    }

    /// <summary>CALL without extension runs TOOL.COM when both TOOL.COM and TOOL.EXE exist.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallWithoutExtensionFallsBackToCom() {
        return Scenario("batch_call_noext_falls_back_to_com", run => run
            .Binary("TOOL.COM", BuildVideoWriterCom('C', 0))
            .Binary("TOOL.EXE", BuildVideoWriterCom('E', 0))
            .StartBatch("CALL TOOL\r\n",
                result => Cell(result, 0).Should().Be('C')));
    }

    /// <summary>The same script as <see cref="CallWithoutExtensionFallsBackToCom"/>, prepared without
    /// TOOL.COM, so the resolution falls through to TOOL.EXE.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallWithoutExtensionFallsBackToExe() {
        return Scenario("batch_call_noext_falls_back_to_exe", run => run
            .Binary("TOOL.EXE", BuildVideoWriterCom('E', 0))
            .StartBatch("CALL TOOL\r\n",
                result => Cell(result, 0).Should().Be('E')));
    }

    /// <summary>The Maupiti Island launcher: a batch file in a nested JEUX\MAUPITI directory redirects
    /// its program output into a file of that same directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MaupitiTatouRunsFromNestedDirectory() {
        return Scenario("batch_maupiti_tatou_nested_directory", run => run
            .TextIn("JEUX/MAUPITI", "TATOU.BAT", "CALL MAUPITIW.COM > C:\\JEUX\\MAUPITI\\MAUPITI.TXT\r\n")
            .BinaryIn("JEUX/MAUPITI", "MAUPITIW.COM", BuildStdoutWriterCom("M"))
            .RunWithoutPit("JEUX/MAUPITI/TATOU.BAT",
                result => run.TextFile("JEUX/MAUPITI/MAUPITI.TXT").Should().Be("M")));
    }

    /// <summary>CALL of a path without extension prefers TATOU.COM over TATOU.BAT and TATOU.EXE in the
    /// referenced directory, and the skipped batch file never runs its ECHO redirection.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario AitdTatouWithoutExtensionPrefersCom() {
        return Scenario("batch_aitd_tatou_noext_prefers_com", run => run
            .TextIn("JEUX/AITD", "TATOU.BAT", "ECHO BAT> C:\\JEUX\\AITD\\WINNER.TXT\r\n")
            .BinaryIn("JEUX/AITD", "TATOU.COM", BuildVideoWriterCom('C', 0))
            .BinaryIn("JEUX/AITD", "TATOU.EXE", BuildVideoWriterCom('E', 0))
            .StartBatch("CALL c:\\jeux\\aitd\\tatou\r\n", result => {
                Cell(result, 0).Should().Be('C');
                run.HostFileExists("JEUX/AITD/WINNER.TXT").Should().BeFalse();
                run.HostFileExists("WINNER.TXT").Should().BeFalse();
            }));
    }

    /// <summary>A batch file CALLing another batch file, which CALLs a COM program.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario NestedBatchCall() {
        return Scenario("batch_nested_batch_call", run => run
            .Binary("WRITER.COM", BuildVideoWriterCom('B', 0))
            .Text("CHILD.BAT", "CALL WRITER.COM\r\n")
            .StartBatch("CALL CHILD.BAT\r\n",
                result => Cell(result, 0).Should().Be('B')));
    }

    /// <summary>The IMGMOUNT internal command must win over an IMGMOUNT.BAT of the same name in the
    /// current directory, so the shadow batch file never runs.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ImgMountInternalCommandPrecedence() {
        return Scenario("batch_imgmount_internal_precedence", run => run
            .Binary("DISK.IMG", new Fat12ImageBuilder().Build())
            .Text("IMGMOUNT.BAT", "echo SHADOW > C:\\SHADOW.TXT\r\n")
            .StartBatch(
                "imgmount a \"C:\\DISK.IMG\" -t floppy\r\n" +
                "if errorlevel 1 echo FAIL > C:\\RESULT.TXT\r\n" +
                "if not errorlevel 1 echo OK > C:\\RESULT.TXT\r\n",
                _ => run.HostFileExists("SHADOW.TXT").Should().BeFalse()));
    }

    /// <summary>GOTO jumps over the commands between it and the label.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario GotoSkipsCommands() {
        return Scenario("batch_goto_skips_commands", run => run
            .Binary("SKIP.COM", BuildVideoWriterCom('S', 2))
            .Binary("PASS.COM", BuildVideoWriterCom('G', 0))
            .StartBatch("GOTO RUN\r\nCALL SKIP.COM\r\n:RUN\r\nCALL PASS.COM\r\n", result => {
                Cell(result, 0).Should().Be('G');
                Cell(result, 1).Should().NotBe('S');
            }));
    }

    /// <summary>After switching the current drive to the Z: memory drive, a relative file lookup resolves
    /// against the memory drive contents and not against the original C: drive.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DriveChangeToZUsesMemoryDrive() {
        return Scenario("batch_drive_change_z_memory_drive", run => run
            .StartBatch(
                "Z:\r\n" +
                "TYPE AUTOEXEC.BAT > C:\\RESULT.TXT\r\n",
                _ => {
                    string output = run.TextFile("RESULT.TXT");
                    output.Should().Contain("CALL C:\\START.BAT",
                        "after switching to Z:, relative file resolution should use the Z: memory drive contents");
                    output.Should().NotContain("File not found",
                        "switching to Z: should not leave the current drive on C:");
                }));
    }

    /// <summary>IF ERRORLEVEL dispatches on the exit code a called program returned.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfErrorLevelDispatchesCommand() {
        return Scenario("batch_if_errorlevel_dispatches", run => run
            .Binary("RET5.COM", BuildExitCodeCom(5))
            .Binary("W5.COM", BuildVideoWriterCom('E', 0))
            .StartBatch("CALL RET5.COM\r\nIF ERRORLEVEL 5 CALL W5.COM\r\n",
                result => Cell(result, 0).Should().Be('E')));
    }

    /// <summary>The "IF ERRORLEVEL = n" syntax dispatches on the exact exit code.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfErrorLevelEqualsSyntaxDispatchesCommand() {
        return Scenario("batch_if_errorlevel_equals_dispatches", run => run
            .Binary("RET10.COM", BuildExitCodeCom(10))
            .Binary("W10.COM", BuildVideoWriterCom('X', 0))
            .StartBatch("CALL RET10.COM\r\nIF ERRORLEVEL = 10 CALL W10.COM\r\n",
                result => Cell(result, 0).Should().Be('X')));
    }

    /// <summary>An Alpha Waves style script routes TESTFKEY.COM function key results to the matching
    /// payload program and then reaches the FIN label.</summary>
    /// <param name="identity">Stable identity of the key variant.</param>
    /// <param name="keyCode">Preloaded BIOS key code of the pressed function key.</param>
    /// <param name="expectedAction">Character the matching payload program writes.</param>
    /// <returns>The scenario, with TESTFKEY.COM as a resource dependency.</returns>
    private static AsmScenario AlphaWavesRoutesFunctionKey(string identity, ushort keyCode, char expectedAction) {
        return ResourceScenario(identity, TestfkeyDependency, run => run
            .ResourceCopy(TestfkeyResourceDirectory, TestfkeyResourceFile, TestfkeyResourceFile)
            .Binary("ALPHA_F.COM", BuildVideoWriterCom('F', 0))
            .Binary("ALPHA_E.COM", BuildVideoWriterCom('E', 0))
            .Binary("ALPHA_D.COM", BuildVideoWriterCom('D', 0))
            .Binary("ALPHA_X.COM", BuildVideoWriterCom('X', 6))
            .StartBatch(AlphaWavesScript, [keyCode], result => {
                Cell(result, 0).Should().Be(expectedAction);
                Cell(result, 3).Should().Be('X');
            }));
    }

    /// <summary>F10 is not one of the routed keys, so the Alpha Waves style script skips every payload
    /// branch and reaches FIN directly.</summary>
    /// <returns>The scenario, with TESTFKEY.COM as a resource dependency.</returns>
    private static AsmScenario AlphaWavesF10BypassesBranches() {
        return ResourceScenario("batch_alpha_waves_f10_bypasses_branches", TestfkeyDependency, run => run
            .ResourceCopy(TestfkeyResourceDirectory, TestfkeyResourceFile, TestfkeyResourceFile)
            .Binary("ALPHA_F.COM", BuildVideoWriterCom('F', 0))
            .Binary("ALPHA_E.COM", BuildVideoWriterCom('E', 0))
            .Binary("ALPHA_D.COM", BuildVideoWriterCom('D', 0))
            .Binary("ALPHA_X.COM", BuildVideoWriterCom('X', 6))
            .StartBatch(AlphaWavesScript, [0x4400], result => {
                Cell(result, 0).Should().NotBe('F');
                Cell(result, 0).Should().NotBe('E');
                Cell(result, 0).Should().NotBe('D');
                Cell(result, 3).Should().Be('X');
            }));
    }

    /// <summary>ECHO keeps the leading spaces of its text when it redirects into a file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoPreservesLeadingSpaces() {
        return Scenario("batch_echo_leading_spaces", run => run
            .StartBatch("ECHO            ALPHA WAVES > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Should().StartWith("           ALPHA WAVES")));
    }

    /// <summary>IF NOT ERRORLEVEL skips the command when the exit code is below the tested value.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfNotErrorLevelSkipsCommand() {
        return Scenario("batch_if_not_errorlevel_skips", run => run
            .Binary("RET5.COM", BuildExitCodeCom(5))
            .Binary("SKIP.COM", BuildVideoWriterCom('S', 0))
            .Binary("PASS.COM", BuildVideoWriterCom('P', 2))
            .StartBatch("CALL RET5.COM\r\nIF NOT ERRORLEVEL 5 CALL SKIP.COM\r\nIF ERRORLEVEL 5 CALL PASS.COM\r\n",
                result => {
                    Cell(result, 0).Should().NotBe('S');
                    Cell(result, 1).Should().Be('P');
                }));
    }

    /// <summary>IF NOT string comparison executes the branch whose operands differ.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfNotStringComparisonExecutesExpectedBranch() {
        return Scenario("batch_if_not_string_comparison", run => run
            .Binary("SKIP.COM", BuildVideoWriterCom('S', 0))
            .Binary("PASS.COM", BuildVideoWriterCom('N', 2))
            .StartBatch("IF NOT \"ONE\"==\"ONE\" CALL SKIP.COM\r\nIF NOT \"ONE\"==\"TWO\" CALL PASS.COM\r\n",
                result => {
                    Cell(result, 0).Should().NotBe('S');
                    Cell(result, 1).Should().Be('N');
                }));
    }

    /// <summary>IF EXIST and IF NOT EXIST take the branch matching the presence of the tested file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfExistAndIfNotExistUseCorrectBranch() {
        return Scenario("batch_if_exist_and_not_exist", run => run
            .Binary("EXISTS.COM", BuildVideoWriterCom('E', 0))
            .Binary("MISSING.COM", BuildVideoWriterCom('M', 2))
            .Text("FLAG.TXT", "1")
            .StartBatch("IF EXIST FLAG.TXT CALL EXISTS.COM\r\nIF NOT EXIST FLAG.TXT CALL MISSING.COM\r\n",
                result => {
                    Cell(result, 0).Should().Be('E');
                    Cell(result, 1).Should().NotBe('M');
                }));
    }

    /// <summary>SHIFT moves the batch arguments so the second argument becomes %1.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ShiftUpdatesArguments() {
        return Scenario("batch_shift_updates_arguments", run => run
            .Binary("W1.COM", BuildVideoWriterCom('1', 0))
            .Binary("W2.COM", BuildVideoWriterCom('2', 2))
            .Text("ROUTE.BAT", "IF \"%1\"==\"ONE\" CALL W1.COM\r\nSHIFT\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\n")
            .StartBatch("CALL ROUTE.BAT ONE TWO\r\n", result => {
                Cell(result, 0).Should().Be('1');
                Cell(result, 1).Should().Be('2');
            }));
    }

    /// <summary>FOR iterates its list and CALLs a batch file once per item.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForIteratesAndCallsBatch() {
        return Scenario("batch_for_iterates", run => run
            .Binary("W1.COM", BuildVideoWriterCom('O', 0))
            .Binary("W2.COM", BuildVideoWriterCom('T', 2))
            .Text("ROUTE.BAT", "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\n")
            .StartBatch("FOR %%I IN(ONE TWO) DO CALL ROUTE.BAT %%I\r\n", result => {
                Cell(result, 0).Should().Be('O');
                Cell(result, 1).Should().Be('T');
            }));
    }

    /// <summary>FOR accepts commas and semicolons as list delimiters.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForSupportsCommaAndSemicolonDelimiters() {
        return Scenario("batch_for_comma_semicolon_delimiters", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .Binary("W3.COM", BuildVideoWriterCom('C', 4))
            .Text("ROUTE.BAT",
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\nIF \"%1\"==\"THREE\" CALL W3.COM\r\n")
            .StartBatch("FOR %%I IN (ONE,TWO;THREE) DO CALL ROUTE.BAT %%I\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().Be('B');
                Cell(result, 2).Should().Be('C');
            }));
    }

    /// <summary>FOR accepts the equals sign as a list delimiter.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForSupportsEqualsDelimiter() {
        return Scenario("batch_for_equals_delimiter", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .Binary("W3.COM", BuildVideoWriterCom('C', 4))
            .Text("ROUTE.BAT",
                "IF \"%1\"==\"ONE\" CALL W1.COM\r\nIF \"%1\"==\"TWO\" CALL W2.COM\r\nIF \"%1\"==\"THREE\" CALL W3.COM\r\n")
            .StartBatch("FOR %%I IN (ONE=TWO=THREE) DO CALL ROUTE.BAT %%I\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().Be('B');
                Cell(result, 2).Should().Be('C');
            }));
    }

    /// <summary>Output redirection writes the program output into the target file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario OutputRedirectionWritesToFile() {
        return Scenario("batch_output_redirection", run => run
            .Binary("OUT.COM", BuildStdoutWriterCom("OK"))
            .StartBatch("CALL OUT.COM > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Should().Be("OK")));
    }

    /// <summary>ECHO keeps the spacing around its redirection operator exactly as written.</summary>
    /// <param name="identity">Stable identity of the spacing variant.</param>
    /// <param name="batchLine">The ECHO line of the batch file.</param>
    /// <param name="expectedOutput">Expected content of OUT.TXT.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoSpacing(string identity, string batchLine, string expectedOutput) {
        return Scenario(identity, run => run
            .StartBatch(batchLine + "\r\n",
                _ => run.TextFile("OUT.TXT").Should().Be(expectedOutput)));
    }

    /// <summary>Append redirection appends to an existing output file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario AppendRedirectionAppendsToFile() {
        return Scenario("batch_append_redirection", run => run
            .Binary("FIRST.COM", BuildStdoutWriterCom("AA"))
            .Binary("SECOND.COM", BuildStdoutWriterCom("BB"))
            .StartBatch("CALL FIRST.COM > OUT.TXT\r\nCALL SECOND.COM >> OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Should().Be("AABB")));
    }

    /// <summary>A pipe transfers the standard output of the producer into the standard input of the
    /// consumer, whatever spacing surrounds the pipe operator.</summary>
    /// <param name="identity">Stable identity of the spacing variant.</param>
    /// <param name="batchLine">The pipeline line of the batch file.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario PipeTransfersStdoutToStdin(string identity, string batchLine) {
        return Scenario(identity, run => run
            .Binary("PROD.COM", BuildStdoutWriterCom("P"))
            .Binary("CONS.COM", BuildStdinToVideoWriterCom(0))
            .StartBatch(batchLine + "\r\n",
                result => Cell(result, 0).Should().Be('P')));
    }

    /// <summary>The first segment of a pipeline accepts an input redirection of its own.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario PipelineSupportsInputRedirectionOnFirstSegment() {
        return Scenario("batch_pipe_input_first_segment", run => run
            .Binary("PASS.COM", BuildStdinToStdoutCom())
            .Binary("CONS.COM", BuildStdinToVideoWriterCom(0))
            .Text("IN.TXT", "Q")
            .StartBatch("CALL PASS.COM < IN.TXT | CALL CONS.COM\r\n",
                result => Cell(result, 0).Should().Be('Q')));
    }

    /// <summary>Input redirection feeds the standard input of the called program.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario InputRedirectionFeedsStdIn() {
        return Scenario("batch_input_redirection", run => run
            .Binary("READ.COM", BuildStdinToVideoWriterCom(0))
            .Text("IN.TXT", "R")
            .StartBatch("CALL READ.COM < IN.TXT\r\n",
                result => Cell(result, 0).Should().Be('R')));
    }

    /// <summary>SET defines a variable that a later IF expands.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario SetUpdatesDosEnvironmentForExpansion() {
        return Scenario("batch_set_env_expansion", run => run
            .Binary("WENV.COM", BuildVideoWriterCom('V', 0))
            .StartBatch("SET SPICE86_BATCH_VAR=YES\r\nIF \"%SPICE86_BATCH_VAR%\"==\"YES\" CALL WENV.COM\r\n",
                result => Cell(result, 0).Should().Be('V')));
    }

    /// <summary>SET with an empty value clears the variable, so the IF expands it to nothing.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario SetEmptyAssignmentClearsVariable() {
        return Scenario("batch_set_empty_clears_env", run => run
            .Binary("WCLEAR.COM", BuildVideoWriterCom('C', 0))
            .StartBatch(
                "SET SPICE86_BATCH_VAR=YES\r\nSET SPICE86_BATCH_VAR=\r\nIF \"%SPICE86_BATCH_VAR%\"==\"\" CALL WCLEAR.COM\r\n",
                result => Cell(result, 0).Should().Be('C')));
    }

    /// <summary>ECHO redirects its text, including the trailing space before the operator, into a file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoWritesToRedirectedFile() {
        return Scenario("batch_echo_redirect", run => run
            .StartBatch("ECHO HELLO > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Should().Be("HELLO \r\n")));
    }

    /// <summary>A line with an invalid redirection operator executes nothing and creates no file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario InvalidRedirectionDoesNotExecuteCommand() {
        return Scenario("batch_invalid_redirect_no_file", run => run
            .StartBatch("ECHO BAD > > OUT.TXT\r\n",
                _ => run.HostFileExists("OUT.TXT").Should().BeFalse()));
    }

    /// <summary>Every invalid redirection operator combination executes nothing and creates no file.</summary>
    /// <param name="identity">Stable identity of the operator combination.</param>
    /// <param name="batchLine">The invalid line of the batch file.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario InvalidRedirectionCreatesNoOutput(string identity, string batchLine) {
        return Scenario(identity, run => run
            .StartBatch(batchLine + "\r\n",
                _ => run.HostFileExists("OUT.TXT").Should().BeFalse()));
    }

    /// <summary>With several output redirections on one line only the last target receives the output.</summary>
    /// <param name="identity">Stable identity of the spacing variant.</param>
    /// <param name="batchLine">The ECHO line of the batch file.</param>
    /// <param name="expectedOutput">Expected content of OUT2.TXT.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario MultipleOutputRedirectionsLastOneWins(string identity, string batchLine,
        string expectedOutput) {
        return Scenario(identity, run => run
            .StartBatch(batchLine + "\r\n", _ => {
                run.TextFile("OUT2.TXT").Should().Be(expectedOutput);
                run.HostFileExists("OUT1.TXT").Should().BeFalse();
            }));
    }

    /// <summary>With several input redirections on one line only the last source feeds the program.</summary>
    /// <param name="identity">Stable identity of the spacing variant.</param>
    /// <param name="batchLine">The pipeline line of the batch file.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario MultipleInputRedirectionsLastOneWins(string identity, string batchLine) {
        return Scenario(identity, run => run
            .Binary("READ.COM", BuildStdinToVideoWriterCom(0))
            .Text("IN1.TXT", "A")
            .Text("IN2.TXT", "B")
            .StartBatch(batchLine + "\r\n",
                result => Cell(result, 0).Should().Be('B')));
    }

    /// <summary>The doubled input operator also feeds the program from the last source only.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DoubleInputOperatorLastOneWins() {
        return Scenario("batch_double_input_operator", run => run
            .Binary("READ.COM", BuildStdinToVideoWriterCom(0))
            .Text("IN1.TXT", "X")
            .Text("IN2.TXT", "Y")
            .StartBatch("CALL READ.COM << IN1.TXT << IN2.TXT\r\n",
                result => Cell(result, 0).Should().Be('Y')));
    }

    /// <summary>An invalid redirection combination must not launch the external command it names.</summary>
    /// <param name="identity">Stable identity of the operator combination.</param>
    /// <param name="batchLine">The invalid line of the batch file.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario InvalidRedirectionLaunchesNoCommand(string identity, string batchLine) {
        return Scenario(identity, run => run
            .Binary("WRITE.COM", BuildVideoWriterCom('Z', 0))
            .Text("IN.TXT", "I")
            .StartBatch(batchLine + "\r\n", result => {
                Cell(result, 0).Should().NotBe('Z');
                run.HostFileExists("OUT.TXT").Should().BeFalse();
            }));
    }

    /// <summary>ECHO with no argument reports its state.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoOffSetsStatusOutput() {
        return Scenario("batch_echo_off_status", run => run
            .StartBatch("ECHO OFF\r\nECHO > STATUS.TXT\r\n",
                _ => run.TextFile("STATUS.TXT").Should().Be("ECHO is OFF.\r\n")));
    }

    /// <summary>ECHO ON after ECHO OFF restores the state report.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoOnAfterOffRestoresStatusOutput() {
        return Scenario("batch_echo_on_status", run => run
            .StartBatch("ECHO OFF\r\nECHO ON\r\nECHO > STATUS.TXT\r\n",
                _ => run.TextFile("STATUS.TXT").Should().Be("ECHO is ON.\r\n")));
    }

    /// <summary>With ECHO ON every batch line is echoed before it executes, so both the command line and
    /// its output are on screen.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoOnByDefaultEchoesEachLine() {
        return Scenario("batch_echo_on_default_echoes_lines", run => run
            .StartBatch("ECHO HELLO\r\n", result => {
                ScreenText(result).Should().Contain("ECHO HELLO",
                    "the ECHO command line itself must be echoed before execution when ECHO is ON");
                ScreenText(result).Should().Contain("HELLO", "the ECHO command output must be displayed");
            }));
    }

    /// <summary>A line prefixed with @ is not echoed even when ECHO is ON, but still executes.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario AtPrefixSuppressesEchoForSingleLine() {
        return Scenario("batch_at_prefix_suppresses_echo", run => run
            .StartBatch("@ECHO HELLO\r\n", result => {
                ScreenText(result).Should().NotContain("ECHO HELLO", "lines prefixed with @ must not be echoed");
                ScreenText(result).Should().Contain("HELLO", "the ECHO command output must still be displayed");
            }));
    }

    /// <summary>After @ECHO OFF no later line is echoed until ECHO ON.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoOffSuppressesSubsequentLineEchoes() {
        return Scenario("batch_echo_off_suppresses_lines", run => run
            .StartBatch("@ECHO OFF\r\nECHO HELLO\r\n", result => {
                ScreenText(result).Should().NotContain("ECHO HELLO",
                    "command lines must not be echoed while ECHO is OFF");
                ScreenText(result).Should().Contain("HELLO", "the ECHO command output must still be displayed");
            }));
    }

    /// <summary>ECHO. prints the text that follows the separator, dropping the separator itself.</summary>
    /// <param name="identity">Stable identity of the separator variant.</param>
    /// <param name="batchLine">The ECHO line of the batch file.</param>
    /// <param name="expectedOutput">Expected content of OUT.TXT.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoDotSeparator(string identity, string batchLine, string expectedOutput) {
        return Scenario(identity, run => run
            .StartBatch(batchLine + "\r\n",
                _ => run.TextFile("OUT.TXT").Should().Be(expectedOutput)));
    }

    /// <summary>An IF with unquoted operands still compares them and executes the command.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfUnquotedComparisonExecutesCommand() {
        return Scenario("batch_if_unquoted_comparison", run => run
            .Binary("WRITE.COM", BuildVideoWriterCom('Q', 0))
            .StartBatch("IF ONE==ONE CALL WRITE.COM\r\n",
                result => Cell(result, 0).Should().Be('Q')));
    }

    /// <summary>A malformed FOR statement is skipped silently and never runs its body.</summary>
    /// <param name="identity">Stable identity of the malformed variant.</param>
    /// <param name="batchLine">The malformed FOR line of the batch file.</param>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForMalformedSyntaxSkips(string identity, string batchLine) {
        return Scenario(identity, run => run
            .Binary("WRITE.COM", BuildVideoWriterCom('Z', 0))
            .StartBatch(batchLine + "\r\n",
                result => Cell(result, 0).Should().NotBe('Z')));
    }

    /// <summary>SET without arguments enumerates the DOS environment into the redirected file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario SetWithoutArgumentsEnumeratesEnvironment() {
        return Scenario("batch_set_enumerates_environment", run => run
            .StartBatch("SET MYVAR=MYVALUE\r\nSET > VARS.TXT\r\n",
                _ => run.TextFile("VARS.TXT").Should().Contain("MYVAR=MYVALUE")));
    }

    /// <summary>The exit code of a called program propagates to the IF ERRORLEVEL that follows it.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ErrorlevelPropagatesFromCalledProgram() {
        return Scenario("batch_errorlevel_propagates", run => run
            .Binary("EXIT42.COM", BuildExitCodeCom(42))
            .Binary("WRITE.COM", BuildVideoWriterCom('E', 0))
            .StartBatch("CALL EXIT42.COM\r\nIF ERRORLEVEL 42 CALL WRITE.COM\r\n",
                result => Cell(result, 0).Should().Be('E')));
    }

    /// <summary>CALLing a batch file keeps the outer batch context, so the lines after the call run.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallNestedBatchPreservesContext() {
        return Scenario("batch_call_nested_batch_context", run => run
            .Binary("WRITE1.COM", BuildVideoWriterCom('A', 0))
            .Binary("WRITE2.COM", BuildVideoWriterCom('B', 2))
            .Text("CHILD.BAT", "CALL WRITE2.COM\r\n")
            .StartBatch("CALL WRITE1.COM\r\nCALL CHILD.BAT\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().Be('B');
            }));
    }

    /// <summary>CALL of a label runs the subroutine and returns to the next line.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallLabelInvokesInternalSubroutine() {
        return Scenario("batch_call_label_subroutine", run => run
            .Binary("W1.COM", BuildVideoWriterCom('X', 0))
            .Binary("W2.COM", BuildVideoWriterCom('Y', 2))
            .StartBatch("CALL :MYSUB\r\nCALL W2.COM\r\n:MYSUB\r\nCALL W1.COM\r\nGOTO :EOF\r\n", result => {
                Cell(result, 0).Should().Be('X');
                Cell(result, 1).Should().Be('Y');
            }));
    }

    /// <summary>A batch subroutine may CALL another subroutine of the same file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallLabelNestedSubroutines() {
        return Scenario("batch_call_label_nested", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .Binary("W3.COM", BuildVideoWriterCom('C', 4))
            .StartBatch(
                "CALL :SUB1\r\nCALL W3.COM\r\n:SUB1\r\nCALL W1.COM\r\nCALL :SUB2\r\nGOTO :EOF\r\n:SUB2\r\nCALL W2.COM\r\nGOTO :EOF\r\n",
                result => {
                    Cell(result, 0).Should().Be('A');
                    Cell(result, 1).Should().Be('B');
                    Cell(result, 2).Should().Be('C');
                }));
    }

    /// <summary>A label subroutine may CALL another batch file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallLabelCallsBatchFile() {
        return Scenario("batch_call_label_with_batch", run => run
            .Binary("P1.COM", BuildVideoWriterCom('A', 0))
            .Binary("P2.COM", BuildVideoWriterCom('B', 2))
            .Text("MYSUB.BAT", "CALL P1.COM\r\nCALL P2.COM\r\n")
            .StartBatch("CALL :TESTS\r\n:TESTS\r\nCALL MYSUB.BAT\r\nGOTO :EOF\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().Be('B');
            }));
    }

    /// <summary>CALL of a label that does not exist returns to the next line.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CallLabelUnknownLabelReturnsToNextLine() {
        return Scenario("batch_call_label_unknown", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .StartBatch("CALL :NOTFOUND\r\nCALL W1.COM\r\nCALL W2.COM\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().Be('B');
            }));
    }

    /// <summary>FOR expands a wildcard to the matching files and calls the route batch once per match.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForWithWildcardExpandsMatchingFiles() {
        return Scenario("batch_for_wildcard_expands", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .Text("ALPHA.TXT", "alpha")
            .Text("BETA.TXT", "beta")
            .Text("ROUTE.BAT",
                "IF \"%1\"==\"ALPHA.TXT\" CALL W1.COM\r\nIF \"%1\"==\"BETA.TXT\" CALL W2.COM\r\n")
            .StartBatch("FOR %%I IN (*.TXT) DO CALL ROUTE.BAT %%I\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().Be('B');
            }));
    }

    /// <summary>FOR over a wildcard with TYPE and append redirection concatenates the matching files.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForWithWildcardTypeRedirect() {
        return Scenario("batch_for_wildcard_type_redirect", run => run
            .Text("A.TXT", "aaa")
            .Text("B.TXT", "bbb")
            .Text("C.DAT", "ccc")
            .StartBatch("FOR %%I IN (*.TXT) DO TYPE %%I >> OUT.TXT\r\n", _ => {
                run.HostFileExists("OUT.TXT").Should().BeTrue("FOR TYPE >> should create the output file");
                string content = run.TextFile("OUT.TXT");
                content.Should().Contain("aaa");
                content.Should().Contain("bbb");
                content.Should().NotContain("ccc");
            }));
    }

    /// <summary>The PATH command defines the search path, which a later IF expands.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario PathCommandSetsEnvironmentForExpansion() {
        return Scenario("batch_path_command_expansion", run => run
            .Binary("WPATH.COM", BuildVideoWriterCom('P', 0))
            .StartBatch("PATH C:\\TOOLS\r\nIF \"%PATH%\"==\"C:\\TOOLS\" CALL WPATH.COM\r\n",
                result => Cell(result, 0).Should().Be('P')));
    }

    /// <summary>PAUSE with a redirected standard input does not block and the batch continues.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario PauseContinuesWithRedirectedStdin() {
        return Scenario("batch_pause_redirected_stdin", run => run
            .Text("KEY.TXT", " ")
            .StartBatch("ECHO BEFORE> BEFORE.TXT\r\nPAUSE < KEY.TXT\r\nECHO AFTER> AFTER.TXT\r\n", _ => {
                run.HostFileExists("BEFORE.TXT").Should().BeTrue();
                run.HostFileExists("AFTER.TXT").Should().BeTrue();
            }));
    }

    /// <summary>FOR over a wildcard without matches runs nothing.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForWildcardWithoutMatchesSkips() {
        return Scenario("batch_for_wildcard_nomatch_skips", run => run
            .Binary("WRITE.COM", BuildVideoWriterCom('Z', 0))
            .StartBatch("FOR %%I IN (*.DAT) DO CALL WRITE.COM\r\n",
                result => Cell(result, 0).Should().NotBe('Z')));
    }

    /// <summary>FOR accepts a list mixing wildcards and literals.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForMixesLiteralsAndWildcards() {
        return Scenario("batch_for_mixed_literals_wildcards", run => run
            .Binary("W1.COM", BuildVideoWriterCom('M', 0))
            .Binary("W2.COM", BuildVideoWriterCom('N', 2))
            .Text("DATA.TXT", "data")
            .Text("ROUTE.BAT",
                "IF \"%1\"==\"DATA.TXT\" CALL W1.COM\r\nIF \"%1\"==\"LITERAL\" CALL W2.COM\r\n")
            .StartBatch("FOR %%I IN (*.TXT LITERAL) DO CALL ROUTE.BAT %%I\r\n", result => {
                Cell(result, 0).Should().Be('M');
                Cell(result, 1).Should().Be('N');
            }));
    }

    /// <summary>A variable set in the outer batch expands inside a CALLed batch file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario VariableExpansionAcrossBatchCalls() {
        return Scenario("batch_variable_expansion_across_calls", run => run
            .Text("CHILD.BAT", "ECHO %SHAREDVAR% > OUTPUT.TXT\r\n")
            .StartBatch("SET SHAREDVAR=SHARED_VALUE\r\nCALL CHILD.BAT\r\n",
                _ => run.TextFile("OUTPUT.TXT").Should().Contain("SHARED_VALUE")));
    }

    /// <summary>CLS clears the screen and the batch continues with the next line.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ClsClearsScreenAndContinues() {
        return Scenario("batch_cls_clears_screen", run => run
            .Binary("WRITE_X.COM", BuildVideoWriterCom('X', 0))
            .Binary("WRITE_Y.COM", BuildVideoWriterCom('Y', 4))
            .StartBatch("@ECHO OFF\r\nCALL WRITE_X.COM\r\nCLS\r\nCALL WRITE_Y.COM\r\n", result => {
                Cell(result, 0).Should().Be(' ', "CLS should have cleared the screen");
                Cell(result, 2).Should().Be('Y', "execution should continue after CLS");
            }));
    }

    /// <summary>CHOICE with the default Y,N choices maps Y to ERRORLEVEL 1.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceDefaultYnSelectsY() {
        return Scenario("batch_choice_default_yn_selects_y", run => run
            .Binary("W1.COM", BuildVideoWriterCom('Y', 0))
            .Text("KEY.TXT", "Y")
            .StartBatch("CHOICE /C:YN < KEY.TXT\r\nIF ERRORLEVEL 1 CALL W1.COM\r\n",
                result => Cell(result, 0).Should().Be('Y')));
    }

    /// <summary>CHOICE maps N to ERRORLEVEL 2, which satisfies both ERRORLEVEL tests, so the first
    /// matching command wins.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceDefaultYnSelectsN() {
        return Scenario("batch_choice_default_yn_selects_n", run => run
            .Binary("W2.COM", BuildVideoWriterCom('N', 0))
            .Binary("SKIP.COM", BuildVideoWriterCom('S', 2))
            .Text("KEY.TXT", "N")
            .StartBatch(
                "CHOICE /C:YN < KEY.TXT\r\n" +
                "IF ERRORLEVEL 2 CALL W2.COM\r\n" +
                "IF ERRORLEVEL 1 CALL SKIP.COM\r\n",
                result => Cell(result, 0).Should().Be('N')));
    }

    /// <summary>CHOICE with custom keys maps the third key to ERRORLEVEL 3.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceCustomKeysSelectsThirdOption() {
        return Scenario("batch_choice_custom_keys_third", run => run
            .Binary("W3.COM", BuildVideoWriterCom('3', 0))
            .Text("KEY.TXT", "C")
            .StartBatch("CHOICE /C:ABC < KEY.TXT\r\nIF ERRORLEVEL 3 CALL W3.COM\r\n",
                result => Cell(result, 0).Should().Be('3')));
    }

    /// <summary>CHOICE matches keys case-insensitively by default.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceIsCaseInsensitiveByDefault() {
        return Scenario("batch_choice_case_insensitive", run => run
            .Binary("W2.COM", BuildVideoWriterCom('2', 0))
            .Text("KEY.TXT", "b")
            .StartBatch("CHOICE /C:ABC < KEY.TXT\r\nIF ERRORLEVEL 2 CALL W2.COM\r\n",
                result => Cell(result, 0).Should().Be('2')));
    }

    /// <summary>CHOICE /S matches keys case-sensitively, so a lowercase key is skipped and the next
    /// keystroke selects.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceWithSFlagIsCaseSensitive() {
        return Scenario("batch_choice_case_sensitive_flag", run => run
            .Binary("W1.COM", BuildVideoWriterCom('1', 0))
            .Binary("W2.COM", BuildVideoWriterCom('2', 0))
            .StartBatch(
                "CHOICE /S /C:AB\r\n" +
                "IF ERRORLEVEL 2 W2.COM\r\n" +
                "IF ERRORLEVEL 1 W1.COM\r\n",
                new ushort[] { 0x1E61, 0x3042 },
                result => Cell(result, 0).Should().Be('2')));
    }

    /// <summary>CHOICE without a choice argument offers the default Y,N keys.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceWithoutChoiceArgumentDefaultsToYN() {
        return Scenario("batch_choice_default_choices_yn", run => run
            .Binary("W1.COM", BuildVideoWriterCom('1', 0))
            .Text("KEY.TXT", "Y")
            .StartBatch("CHOICE < KEY.TXT\r\nIF ERRORLEVEL 1 CALL W1.COM\r\n",
                result => Cell(result, 0).Should().Be('1')));
    }

    /// <summary>CHOICE /N suppresses the [Y,N]? prompt in the redirected output.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceWithNFlagWritesNoPrompt() {
        return Scenario("batch_choice_n_flag_prompt", run => run
            .Text("KEY.TXT", "Y")
            .StartBatch("CHOICE /N /C:YN Choose: > PROMPT.TXT < KEY.TXT\r\n",
                _ => run.TextFile("PROMPT.TXT").Should().NotContain("[Y,N]?")));
    }

    /// <summary>CHOICE writes its prompt text and the default [Y,N]? suffix into the redirected output.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceWithPromptTextWritesPrompt() {
        return Scenario("batch_choice_prompt_text", run => run
            .Text("KEY.TXT", "Y")
            .StartBatch("CHOICE /C:YN Continue > PROMPT.TXT < KEY.TXT\r\n",
                _ => run.TextFile("PROMPT.TXT").Should().Contain("[Y,N]?")));
    }

    /// <summary>CHOICE /T with no keypress selects the timeout default.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceTimeoutSelectsDefaultOnEof() {
        return Scenario("batch_choice_timeout_default", run => run
            .Binary("W1.COM", BuildVideoWriterCom('T', 0))
            .StartBatch("CHOICE /C:YN /T:Y,5\r\nIF ERRORLEVEL 1 CALL W1.COM\r\n",
                result => Cell(result, 0).Should().Be('T')));
    }

    /// <summary>CHOICE /T uses the pressed key instead of the timeout default when one arrives.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceTimeoutKeypressTakesPrecedence() {
        return Scenario("batch_choice_timeout_keypress", run => run
            .Binary("WA.COM", BuildVideoWriterCom('A', 0))
            .Binary("WB.COM", BuildVideoWriterCom('B', 2))
            .Text("KEY.TXT", "A")
            .StartBatch(
                "CHOICE /C:AB /T:B,3 < KEY.TXT\r\n" +
                "IF ERRORLEVEL 2 CALL WB.COM\r\n" +
                "IF ERRORLEVEL 1 CALL WA.COM\r\n",
                result => Cell(result, 0).Should().Be('A')));
    }

    /// <summary>A Jill of the Jungle style menu routes the CHOICE result through an ERRORLEVEL GOTO chain
    /// to the matching game section.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario JillMenuRoutesViaErrorlevelGotoChain() {
        return Scenario("batch_jill_menu_errorlevel_goto", run => run
            .Binary("GAME.COM", BuildVideoWriterCom('G', 0))
            .Binary("STORY.COM", BuildVideoWriterCom('S', 2))
            .Binary("ORDER.COM", BuildVideoWriterCom('O', 4))
            .Binary("EXIT.COM", BuildExitCodeCom(0))
            .Text("KEY.TXT", "2")
            .StartBatch(JillMenuScript, result => {
                Cell(result, 0).Should().NotBe('G', "should not reach GAME");
                Cell(result, 1).Should().Be('S', "should reach STORY (choice 2)");
                Cell(result, 2).Should().NotBe('O', "should not reach ORDER");
            }));
    }

    /// <summary>TYPE displays the contents of an existing file into the redirected output.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario TypeDisplaysFileContents() {
        return Scenario("batch_type_displays_file", run => run
            .Text("HELLO.TXT", "Hello World")
            .StartBatch("TYPE HELLO.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Should().Be("Hello World")));
    }

    /// <summary>TYPE on a missing file reports the error instead of its content.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario TypeMissingFileWritesError() {
        return Scenario("batch_type_missing_file_error", run => run
            .StartBatch("TYPE NOFILE.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Should().Contain("not found",
                    "TYPE on a missing file should report an error")));
    }

    /// <summary>CD without arguments prints the current directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CdWithoutArgsPrintsCurrentDirectory() {
        return Scenario("batch_cd_no_args", run => run
            .StartBatch("CD > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().StartWith("C:\\",
                    "CD with no args should print the current drive and directory")));
    }

    /// <summary>CD with a path changes the current directory of the batch.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CdChangesDirectory() {
        return Scenario("batch_cd_changes_directory", run => run
            .Folder("SUBDIR")
            .StartBatch("CD SUBDIR\r\nCD > OUT.TXT\r\n",
                _ => run.TextFile("SUBDIR/OUT.TXT").Trim().Should().Contain("SUBDIR",
                    "CD should have changed to the subdirectory")));
    }

    /// <summary>CD.. without a space navigates to the parent directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CdDotDotNavigatesToParent() {
        return Scenario("batch_cd_dotdot_parent", run => run
            .Folder("CHILD")
            .StartBatch("CD CHILD\r\nCD..\r\nCD > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotContain("CHILD",
                    "CD.. should navigate to the parent directory")));
    }

    /// <summary>CD. without a space stays in the current directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CdDotStaysInCurrentDirectory() {
        return Scenario("batch_cd_dot_stays", run => run
            .Folder("SUB")
            .StartBatch("CD SUB\r\nCD.\r\nCD > OUT.TXT\r\n",
                _ => run.TextFile("SUB/OUT.TXT").Trim().Should().Contain("SUB",
                    "CD. should stay in the current directory")));
    }

    /// <summary>CD\ without a space navigates to the root directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CdBackslashNavigatesToRoot() {
        return Scenario("batch_cd_backslash_root", run => run
            .Folder("DEEP")
            .StartBatch("CD DEEP\r\nCD\\\r\nCD > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotContain("DEEP",
                    "CD\\ should navigate to the root directory")));
    }

    /// <summary>IF string comparison is case-sensitive, matching DOS strcmp behaviour.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfStringComparisonIsCaseSensitive() {
        return Scenario("batch_if_string_case_sensitive", run => run
            .Binary("MISS.COM", BuildVideoWriterCom('M', 0))
            .Binary("EXACT.COM", BuildVideoWriterCom('E', 0))
            .Binary("BAD.COM", BuildVideoWriterCom('B', 2))
            .StartBatch("IF hello==HELLO CALL BAD.COM\r\nIF hello==hello CALL EXACT.COM\r\n", result => {
                Cell(result, 0).Should().Be('E', "exact case match should execute");
                Cell(result, 1).Should().NotBe('B', "case-insensitive match should NOT execute");
            }));
    }

    /// <summary>IF keeps the surrounding quotes as part of the compared token.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario IfStringComparisonPreservesQuotes() {
        return Scenario("batch_if_string_preserves_quotes", run => run
            .Binary("WRITE.COM", BuildVideoWriterCom('W', 0))
            .StartBatch("IF \"A B\"==\"A B\" CALL WRITE.COM\r\n",
                result => Cell(result, 0).Should().Be('W')));
    }

    /// <summary>GOTO to a label that does not exist stops the batch file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario GotoMissingLabelStopsBatch() {
        return Scenario("batch_goto_missing_label_stops", run => run
            .Binary("NEVER.COM", BuildVideoWriterCom('N', 0))
            .StartBatch("GOTO NONEXIST\r\nCALL NEVER.COM\r\n",
                result => Cell(result, 0).Should().NotBe('N')));
    }

    /// <summary>TYPE stops reading at the 0x1A end of file marker.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario TypeStopsAtCtrlZEof() {
        return Scenario("batch_type_stops_at_ctrl_z", run => run
            .Binary("DATA.TXT", new byte[] { 0x41, 0x42, 0x1A, 0x43, 0x44 })
            .StartBatch("TYPE DATA.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Should().Be("AB", "TYPE should stop at 0x1A EOF marker")));
    }

    /// <summary>CHOICE skips keys outside its choice set and accepts the first valid key.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceSkipsInvalidKeys() {
        return Scenario("batch_choice_skips_invalid_keys", run => run
            .Binary("W2.COM", BuildVideoWriterCom('2', 0))
            .Text("KEY.TXT", "XN")
            .StartBatch("CHOICE /C:YN < KEY.TXT\r\nIF ERRORLEVEL 2 CALL W2.COM\r\n",
                result => Cell(result, 0).Should().Be('2')));
    }

    /// <summary>ECHO OFF inside a CALLed batch file does not change the echo state of the outer batch.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EchoOffInInnerBatchDoesNotAffectOuter() {
        return Scenario("batch_echo_off_inner_batch", run => run
            .Text("INNER.BAT", "@ECHO OFF\r\n")
            .StartBatch("ECHO ON\r\nCALL INNER.BAT\r\nECHO > STATUS.TXT\r\n",
                _ => run.TextFile("STATUS.TXT").Should().Be("ECHO is ON.\r\n")));
    }

    /// <summary>The batch reader stops at the 0x1A marker, so the lines after it never run.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario BatchFileReadingStopsAtCtrlZ() {
        return Scenario("batch_reading_stops_at_ctrl_z", run => run
            .Binary("W1.COM", BuildVideoWriterCom('H', 0))
            .Binary("W2.COM", BuildVideoWriterCom('W', 2))
            .Binary("TEST.BAT", Encoding.ASCII.GetBytes(
                "CALL W1.COM\r\n" +
                "\x1a" +
                "CALL W2.COM\r\n"))
            .Run("TEST.BAT", result => {
                Cell(result, 0).Should().Be('H');
                Cell(result, 1).Should().NotBe('W');
            }));
    }

    /// <summary>EXIT terminates the current batch file, so the lines after it never run.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ExitStopsExecution() {
        return Scenario("batch_exit_stops_execution", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .StartBatch("W1\r\nEXIT\r\nW2\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().NotBe('B');
            }));
    }

    /// <summary>EXIT inside a CALLed batch file stops every batch context, not only the inner file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ExitInsideCallStopsAllBatches() {
        return Scenario("batch_exit_inside_call_stops_all", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .Text("INNER.BAT", "W1\r\nEXIT\r\n")
            .StartBatch("CALL INNER.BAT\r\nW2\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().NotBe('B');
            }));
    }

    /// <summary>Invoking a batch file without CALL replaces the current batch context.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario BareBatInvocationReplacesCurrentBatch() {
        return Scenario("batch_bare_bat_replaces_context", run => run
            .Binary("W1.COM", BuildVideoWriterCom('A', 0))
            .Binary("W2.COM", BuildVideoWriterCom('B', 2))
            .Text("SUB.BAT", "W1\r\n")
            .StartBatch("SUB.BAT\r\nW2\r\n", result => {
                Cell(result, 0).Should().Be('A');
                Cell(result, 1).Should().NotBe('B',
                    "bare .BAT invocation should replace current batch; W2 should not execute");
            }));
    }

    /// <summary>Invoking a missing batch file without CALL keeps the current context.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario BareMissingBatKeepsCurrentContext() {
        return Scenario("batch_bare_missing_bat_keeps_context", run => run
            .Binary("PASS.COM", BuildVideoWriterCom('P', 0))
            .StartBatch("MISSING.BAT\r\nCALL PASS.COM\r\n",
                result => Cell(result, 0).Should().Be('P',
                    "missing bare batch invocation should not abort remaining parent batch lines")));
    }

    /// <summary>The FOR list parser ignores a right parenthesis inside a quoted item.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForListParsesQuotedRightParenthesis() {
        return Scenario("batch_for_quoted_parenthesis", run => run
            .StartBatch("FOR %%I IN (\"A B\" \"C,D\" \"E;F\" \"A)B\") DO ECHO %%I>>OUT.TXT\r\n",
                _ => {
                    string output = run.TextFile("OUT.TXT");
                    output.Should().Contain("\"A B\"");
                    output.Should().Contain("\"C,D\"");
                    output.Should().Contain("\"E;F\"");
                    output.Should().Contain("A)B");
                }));
    }

    /// <summary>LH is stripped and the rest of the command line executes normally.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario LhPassesThrough() {
        return Scenario("batch_lh_passes_through", run => run
            .Text("SRC.TXT", "lh content")
            .StartBatch("LH TYPE SRC.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().Be("lh content")));
    }

    /// <summary>LOADHIGH is stripped and the rest of the command line executes normally.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario LoadhighPassesThrough() {
        return Scenario("batch_loadhigh_passes_through", run => run
            .Text("SRC.TXT", "loadhigh content")
            .StartBatch("LOADHIGH TYPE SRC.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().Be("loadhigh content")));
    }

    /// <summary>MD creates a directory that CD can then enter.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MdCreatesDirectory() {
        return Scenario("batch_md_creates_directory", run => run
            .StartBatch("MD NEWDIR\r\nCD NEWDIR\r\nCD > OUT.TXT\r\n",
                _ => run.TextFile("NEWDIR/OUT.TXT").Trim().Should().Contain("NEWDIR",
                    "MD should have created the directory")));
    }

    /// <summary>MKDIR creates a directory, as an alias of MD.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MkdirIsAliasForMd() {
        return Scenario("batch_mkdir_alias", run => run
            .StartBatch("MKDIR SUBDIR\r\nCD SUBDIR\r\nCD > OUT.TXT\r\n",
                _ => run.TextFile("SUBDIR/OUT.TXT").Trim().Should().Contain("SUBDIR")));
    }

    /// <summary>MD on an existing directory reports an error.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MdOnExistingDirectoryPrintsError() {
        return Scenario("batch_md_existing_error", run => run
            .Folder("EXISTING")
            .StartBatch("MD EXISTING > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotBeEmpty(
                    "MD on an existing directory should report an error")));
    }

    /// <summary>RD removes an empty directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RdRemovesEmptyDirectory() {
        return Scenario("batch_rd_removes_directory", run => run
            .Folder("TODEL")
            .StartBatch("RD TODEL\r\n",
                _ => run.HostDirectoryExists("TODEL").Should().BeFalse("RD should remove the empty directory")));
    }

    /// <summary>RMDIR removes an empty directory, as an alias of RD.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RmdirIsAliasForRd() {
        return Scenario("batch_rmdir_alias", run => run
            .Folder("GONE")
            .StartBatch("RMDIR GONE\r\n",
                _ => run.HostDirectoryExists("GONE").Should().BeFalse()));
    }

    /// <summary>RD on a directory that does not exist reports an error.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RdOnMissingDirectoryPrintsError() {
        return Scenario("batch_rd_missing_error", run => run
            .StartBatch("RD NOPE > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotBeEmpty(
                    "RD on a non-existent directory should report an error")));
    }

    /// <summary>DEL removes a single file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DelRemovesSingleFile() {
        return Scenario("batch_del_single_file", run => run
            .Text("KILL.TXT", "doomed")
            .StartBatch("DEL KILL.TXT\r\n",
                _ => run.HostFileExists("KILL.TXT").Should().BeFalse("DEL should remove the specified file")));
    }

    /// <summary>ERASE removes a single file, as an alias of DEL.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario EraseIsAliasForDel() {
        return Scenario("batch_erase_alias", run => run
            .Text("GONE.TXT", "bye")
            .StartBatch("ERASE GONE.TXT\r\n",
                _ => run.HostFileExists("GONE.TXT").Should().BeFalse()));
    }

    /// <summary>DEL with a wildcard removes every matching file and keeps the others.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DelWildcardRemovesMatchingFiles() {
        return Scenario("batch_del_wildcard", run => run
            .Text("A.TMP", "a")
            .Text("B.TMP", "b")
            .Text("KEEP.TXT", "keep")
            .StartBatch("DEL *.TMP\r\n", _ => {
                run.HostFileExists("A.TMP").Should().BeFalse();
                run.HostFileExists("B.TMP").Should().BeFalse();
                run.HostFileExists("KEEP.TXT").Should().BeTrue("DEL *.TMP should not remove .TXT files");
            }));
    }

    /// <summary>DEL on a missing file reports the error.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DelOnMissingFilePrintsError() {
        return Scenario("batch_del_missing_error", run => run
            .StartBatch("DEL NOFILE.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().Contain("not found",
                    "DEL on a missing file should report file not found")));
    }

    /// <summary>DEL with a wildcard removes the matching files and leaves directories alone.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DelSkipsDirectories() {
        return Scenario("batch_del_skips_directories", run => run
            .Folder("SUBDIR")
            .Text("FILE.TXT", "file")
            .StartBatch("DEL *.TXT\r\n", _ => {
                run.HostDirectoryExists("SUBDIR").Should().BeTrue("DEL should not remove directories");
                run.HostFileExists("FILE.TXT").Should().BeFalse("DEL *.TXT should remove the .TXT file");
            }));
    }

    /// <summary>DEL skips read-only files and removes the writable ones.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DelSkipsReadonlyFiles() {
        return Scenario("batch_del_skips_readonly", run => {
            run.Text("LOCKED.TXT", "protected");
            File.SetAttributes(run.PathOf("LOCKED.TXT"), FileAttributes.ReadOnly);
            return run
                .Text("NORMAL.TXT", "deletable")
                .StartBatch("DEL *.TXT\r\n", _ => {
                    run.HostFileExists("LOCKED.TXT").Should().BeTrue("DEL should skip read-only files");
                    run.HostFileExists("NORMAL.TXT").Should().BeFalse("DEL should remove normal files");
                });
        });
    }

    /// <summary>REN renames a single file, keeping its content.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RenRenamesSingleFile() {
        return Scenario("batch_ren_single_file", run => run
            .Text("OLD.TXT", "content")
            .StartBatch("REN OLD.TXT NEW.TXT\r\n", _ => {
                run.HostFileExists("OLD.TXT").Should().BeFalse();
                run.HostFileExists("NEW.TXT").Should().BeTrue();
                run.TextFile("NEW.TXT").Should().Be("content");
            }));
    }

    /// <summary>RENAME renames a single file, as an alias of REN.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RenameIsAliasForRen() {
        return Scenario("batch_rename_alias", run => run
            .Text("A.TXT", "data")
            .StartBatch("RENAME A.TXT B.TXT\r\n", _ => {
                run.HostFileExists("A.TXT").Should().BeFalse();
                run.HostFileExists("B.TXT").Should().BeTrue();
            }));
    }

    /// <summary>REN on a source file that does not exist reports an error.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RenOnMissingFilePrintsError() {
        return Scenario("batch_ren_missing_error", run => run
            .StartBatch("REN NOFILE.TXT NEW.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotBeEmpty(
                    "REN on a missing file should report an error")));
    }

    /// <summary>REN without a target name reports a syntax error.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RenWithoutTargetPrintsError() {
        return Scenario("batch_ren_missing_target", run => run
            .Text("FILE.TXT", "data")
            .StartBatch("REN FILE.TXT > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotBeEmpty(
                    "REN with missing target should report a syntax error")));
    }

    /// <summary>REN with wildcard source and target renames every matching file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RenWildcardSourceAndTarget() {
        return Scenario("batch_ren_wildcard", run => run
            .Text("A.TXT", "aaa")
            .Text("B.TXT", "bbb")
            .Text("C.DAT", "ccc")
            .StartBatch("REN *.TXT *.BAK\r\n", _ => {
                run.HostFileExists("A.TXT").Should().BeFalse();
                run.HostFileExists("A.BAK").Should().BeTrue();
                run.HostFileExists("B.TXT").Should().BeFalse();
                run.HostFileExists("B.BAK").Should().BeTrue();
                run.HostFileExists("C.DAT").Should().BeTrue("non-matching file should remain");
            }));
    }

    /// <summary>REN with a wildcard that matches nothing does nothing and the batch continues.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RenWildcardWithoutMatches() {
        return Scenario("batch_ren_wildcard_no_matches", run => run
            .Text("DATA.DAT", "data")
            .StartBatch("REN *.TXT *.BAK\r\nECHO DONE > DONE.TXT\r\n", _ => {
                run.HostFileExists("DONE.TXT").Should().BeTrue("batch should continue");
                run.HostFileExists("DATA.DAT").Should().BeTrue();
            }));
    }

    /// <summary>VOL with a drive argument prints the volume information of that drive.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario VolWithDriveArgument() {
        return Scenario("batch_vol_drive_argument", run => run
            .StartBatch("VOL C: > VOL.TXT\r\n",
                _ => run.TextFile("VOL.TXT").Should().NotBeEmpty("VOL C: should produce output")));
    }

    /// <summary>FOR over a wildcard deletes every matching file and keeps the others.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForWildcardExpansionDeletesMatchingFiles() {
        return Scenario("batch_for_wildcard_del", run => run
            .Text("A.TXT", "aaa")
            .Text("B.TXT", "bbb")
            .Text("C.TXT", "ccc")
            .Text("SKIP.DAT", "nope")
            .StartBatch("FOR %%I IN (*.TXT) DO DEL %%I\r\n", _ => {
                run.HostFileExists("A.TXT").Should().BeFalse("FOR should have expanded *.TXT and deleted A.TXT");
                run.HostFileExists("B.TXT").Should().BeFalse("FOR should have expanded *.TXT and deleted B.TXT");
                run.HostFileExists("C.TXT").Should().BeFalse("FOR should have expanded *.TXT and deleted C.TXT");
                run.HostFileExists("SKIP.DAT").Should().BeTrue("SKIP.DAT should not match *.TXT");
            }));
    }

    /// <summary>FOR over a wildcard without matches executes nothing, creates no output file and lets the
    /// batch continue.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForWildcardWithoutTypeMatchesCreatesNoOutput() {
        return Scenario("batch_for_wildcard_no_matches_type", run => run
            .Text("SKIP.DAT", "data")
            .StartBatch("FOR %%I IN (*.TXT) DO TYPE %%I >> OUT.TXT\r\nECHO DONE > DONE.TXT\r\n", _ => {
                run.TextFile("DONE.TXT").Trim().Should().Be("DONE");
                run.HostFileExists("OUT.TXT").Should().BeFalse();
            }));
    }

    /// <summary>DIR lists the files of the current directory with their names.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirListsFiles() {
        return Scenario("batch_dir_lists_files", run => run
            .Text("HELLO.TXT", "Hello World")
            .StartBatch("DIR > OUT.TXT\r\n", _ => {
                string output = run.TextFile("OUT.TXT").ToUpperInvariant();
                output.Should().Contain("HELLO");
                output.Should().Contain("TXT");
            }));
    }

    /// <summary>DIR /B prints the bare file names, one per line, without duplicating an entry.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirBareFormat() {
        return Scenario("batch_dir_bare_format", run => run
            .Text("A.TXT", "a")
            .Text("B.DAT", "b")
            .StartBatch("DIR /B > OUT.TXT\r\n", _ => {
                string[] lines = OutputLines(run.TextFile("OUT.TXT"));
                lines.Should().Contain(line => line.Contains("A.TXT", StringComparison.OrdinalIgnoreCase));
                lines.Should().Contain(line => line.Contains("B.DAT", StringComparison.OrdinalIgnoreCase));
                lines.Count(line => string.Equals(line, "A.TXT", StringComparison.OrdinalIgnoreCase)).Should().Be(1,
                    "DIR /B should not duplicate the first matching entry");
                lines.Count(line => string.Equals(line, "B.DAT", StringComparison.OrdinalIgnoreCase)).Should().Be(1,
                    "DIR /B should not duplicate entries");
            }));
    }

    /// <summary>DIR with a file pattern lists only the matching files.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirWithPattern() {
        return Scenario("batch_dir_with_pattern", run => run
            .Text("A.TXT", "a")
            .Text("B.TXT", "b")
            .Text("C.DAT", "c")
            .StartBatch("DIR /B *.TXT > OUT.TXT\r\n", _ => {
                string output = run.TextFile("OUT.TXT").ToUpperInvariant();
                output.Should().Contain("A.TXT");
                output.Should().Contain("B.TXT");
                output.Should().NotContain("C.DAT");
            }));
    }

    /// <summary>COPY copies one file to a new name.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CopySingleFile() {
        return Scenario("batch_copy_single_file", run => run
            .Text("SRC.TXT", "hello copy")
            .StartBatch("COPY SRC.TXT DST.TXT\r\n",
                _ => run.TextFile("DST.TXT").Should().Be("hello copy")));
    }

    /// <summary>COPY with a wildcard copies every matching file into the target directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CopyWildcard() {
        return Scenario("batch_copy_wildcard", run => run
            .Folder("SUB")
            .Text("A.TXT", "aaa")
            .Text("B.TXT", "bbb")
            .Text("C.DAT", "ccc")
            .StartBatch("COPY *.TXT SUB > COPYLOG.LOG\r\n", _ => {
                run.TextFile("SUB/A.TXT").Should().Be("aaa");
                run.TextFile("SUB/B.TXT").Should().Be("bbb");
                run.HostFileExists("SUB/C.DAT").Should().BeFalse();
                run.TextFile("COPYLOG.LOG").Should().Contain("2 file(s) copied");
            }));
    }

    /// <summary>COPY accepts the /Y and /V switches.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CopySwitchesAreAccepted() {
        return Scenario("batch_copy_switches", run => run
            .Text("SRC.TXT", "copy switches")
            .StartBatch("COPY /Y /V SRC.TXT DST.TXT\r\n",
                _ => run.TextFile("DST.TXT").Should().Be("copy switches")));
    }

    /// <summary>COPY of a missing source reports an error and creates no destination file.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CopyOfMissingSource() {
        return Scenario("batch_copy_missing_source", run => run
            .StartBatch("COPY NOSRC.TXT DST.TXT > OUT.TXT\r\n", _ => {
                run.TextFile("OUT.TXT").Trim().Should().NotBeEmpty(
                    "COPY of missing file should produce an error message");
                run.HostFileExists("DST.TXT").Should().BeFalse();
            }));
    }

    /// <summary>COPY of one file into a directory places the copy inside it.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CopySingleFileToDirectory() {
        return Scenario("batch_copy_to_directory", run => run
            .Folder("DEST")
            .Text("SRC.TXT", "hello dir copy")
            .StartBatch("COPY SRC.TXT DEST\r\n", _ => {
                run.HostFileExists("DEST/SRC.TXT").Should().BeTrue(
                    "COPY to a directory should place the file inside it");
                run.TextFile("DEST/SRC.TXT").Should().Be("hello dir copy");
            }));
    }

    /// <summary>DATE /T prints a date string.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DatePrintsDate() {
        return Scenario("batch_date_prints", run => run
            .StartBatch("DATE /T > OUT.TXT\r\n", _ => {
                string output = run.TextFile("OUT.TXT").Trim();
                output.Should().NotBeEmpty();
                output.Should().MatchRegex(@"\d{4}");
            }));
    }

    /// <summary>DATE with a value changes the date the following DATE /T prints.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DateSetValueChangesDisplayedDate() {
        return Scenario("batch_date_set_value", run => run
            .StartBatch("DATE 02-29-2024\r\nDATE /T > OUT.TXT\r\n", _ => {
                string output = run.TextFile("OUT.TXT").Trim();
                output.Should().Contain("2024");
                output.Should().Contain("02-29");
            }));
    }

    /// <summary>DATE /H synchronizes from the host and the following DATE /T prints the date.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DateHostSyncProducesDateOutput() {
        return Scenario("batch_date_host_sync", run => run
            .StartBatch("DATE /H\r\nDATE /T > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().MatchRegex(@"\d{2}-\d{2}-\d{4}")));
    }

    /// <summary>TIME /T prints a time string with hour and minute.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario TimePrintsTime() {
        return Scenario("batch_time_prints", run => run
            .StartBatch("TIME /T > OUT.TXT\r\n", _ => {
                string output = run.TextFile("OUT.TXT").Trim();
                output.Should().NotBeEmpty();
                output.Should().MatchRegex(@"\d+:\d+");
            }));
    }

    /// <summary>TIME with a value changes the time the following TIME /T prints.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario TimeSetValueChangesDisplayedTime() {
        return Scenario("batch_time_set_value", run => run
            .StartBatch("TIME 13:14:15\r\nTIME /T > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().Contain("13:14:15")));
    }

    /// <summary>TIME /H synchronizes from the host and the following TIME /T prints the time.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario TimeHostSyncProducesTimeOutput() {
        return Scenario("batch_time_host_sync", run => run
            .StartBatch("TIME /H\r\nTIME /T > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().MatchRegex(@"\d{2}:\d{2}:\d{2}")));
    }

    /// <summary>VER prints a version string.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario VerPrintsVersion() {
        return Scenario("batch_ver_prints", run => run
            .StartBatch("VER > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotBeEmpty()));
    }

    /// <summary>VOL prints the volume label of the current drive.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario VolPrintsVolumeLabel() {
        return Scenario("batch_vol_prints_label", run => run
            .StartBatch("VOL > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").Trim().Should().NotBeEmpty()));
    }

    /// <summary>VOL on a mounted CD-ROM drive prints the label of the mounted media.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario VolOnMountedCdDrivePrintsMediaLabel() {
        return Scenario("batch_vol_cdrom_label", run => {
            string cdRoot = run.PathOf("MYDISC");
            run.Folder("MYDISC");
            return run.StartBatch(
                $"MOUNT D \"{cdRoot}\" -t cdrom\r\n" +
                "D:\r\n" +
                "VOL > C:\\OUT.TXT\r\n",
                _ => {
                    string output = run.TextFile("OUT.TXT").ToUpperInvariant();
                    output.Should().Contain("VOLUME IN DRIVE D IS MYDISC",
                        "VOL on a mounted CD drive should print the mounted media label, not the default drive placeholder label");
                });
        });
    }

    /// <summary>DATE /? prints its usage text.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DateHelpSwitchPrintsUsage() {
        return Scenario("batch_date_help", run => run
            .StartBatch("DATE /? > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").ToUpperInvariant().Should().Contain("DATE")));
    }

    /// <summary>TIME /? prints its usage text.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario TimeHelpSwitchPrintsUsage() {
        return Scenario("batch_time_help", run => run
            .StartBatch("TIME /? > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").ToUpperInvariant().Should().Contain("TIME")));
    }

    /// <summary>DIR /B /AD lists the directories only.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirAdListsDirectoriesOnly() {
        return Scenario("batch_dir_ad_directories", run => run
            .Folder("SUBDIR")
            .Text("FILE.TXT", "x")
            .StartBatch("DIR /B /AD > OUT.TXT\r\n", _ => {
                string output = run.TextFile("OUT.TXT").ToUpperInvariant();
                output.Should().Contain("SUBDIR");
                output.Should().NotContain("FILE.TXT");
            }));
    }

    /// <summary>DIR /B /A-D lists the files only.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirAMinusDListsFilesOnly() {
        return Scenario("batch_dir_a_minus_d_files", run => run
            .Folder("SUBDIR")
            .Text("FILE.TXT", "x")
            .StartBatch("DIR /B /A-D > OUT.TXT\r\n", _ => {
                string output = run.TextFile("OUT.TXT").ToUpperInvariant();
                output.Should().Contain("FILE.TXT");
                output.Should().NotContain("SUBDIR");
            }));
    }

    /// <summary>DIR /B /ON sorts the listed names by ascending name.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirOnSortsByNameAscending() {
        return Scenario("batch_dir_sort_on", run => run
            .Text("B.TXT", "b")
            .Text("A.TXT", "a")
            .StartBatch("DIR /B /ON *.TXT > OUT.TXT\r\n", _ => {
                string[] lines = OutputLines(run.TextFile("OUT.TXT"));
                int aIndex = Array.FindIndex(lines, line => line.Equals("A.TXT", StringComparison.OrdinalIgnoreCase));
                int bIndex = Array.FindIndex(lines, line => line.Equals("B.TXT", StringComparison.OrdinalIgnoreCase));
                aIndex.Should().BeGreaterThanOrEqualTo(0);
                bIndex.Should().BeGreaterThanOrEqualTo(0);
                aIndex.Should().BeLessThan(bIndex);
            }));
    }

    /// <summary>DIR /B /O-N sorts the listed names by descending name.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DirOMinusNSortsByNameDescending() {
        return Scenario("batch_dir_sort_o_minus_n", run => run
            .Text("B.TXT", "b")
            .Text("A.TXT", "a")
            .StartBatch("DIR /B /O-N *.TXT > OUT.TXT\r\n", _ => {
                string[] lines = OutputLines(run.TextFile("OUT.TXT"));
                int aIndex = Array.FindIndex(lines, line => line.Equals("A.TXT", StringComparison.OrdinalIgnoreCase));
                int bIndex = Array.FindIndex(lines, line => line.Equals("B.TXT", StringComparison.OrdinalIgnoreCase));
                aIndex.Should().BeGreaterThanOrEqualTo(0);
                bIndex.Should().BeGreaterThanOrEqualTo(0);
                bIndex.Should().BeLessThan(aIndex);
            }));
    }

    /// <summary>SET /P reports that the prompt option is unsupported.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario SetPromptOptionPrintsUnsupportedMessage() {
        return Scenario("batch_set_prompt_unsupported", run => run
            .StartBatch("SET /P MYVAR=QUESTION? > OUT.TXT\r\n",
                _ => run.TextFile("OUT.TXT").ToUpperInvariant().Should().Contain("UNSUPPORTED")));
    }

    /// <summary>FOR over a bare star matches every file, whatever its extension.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ForBareStarMatchesAllFiles() {
        return Scenario("batch_for_bare_star", run => run
            .Text("A.TXT", "aaa")
            .Text("B.DAT", "bbb")
            .StartBatch("FOR %%I IN (*) DO TYPE %%I >> OUT.TXT\r\n", _ => {
                run.HostFileExists("OUT.TXT").Should().BeTrue("FOR with bare * should match files");
                string content = run.TextFile("OUT.TXT");
                content.Should().Contain("aaa", "bare * must match .TXT files");
                content.Should().Contain("bbb", "bare * must match .DAT files");
            }));
    }

    /// <summary>A batch file found through the PATH search is executed from the current directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario PathSearchFindsExternalBat() {
        return Scenario("batch_path_search_finds_bat", run => run
            .TextIn("TOOLS", "TOOL.BAT", "ECHO FOUND > RESULT.TXT\r\n")
            .StartBatch("PATH C:\\TOOLS\r\nTOOL\r\n",
                _ => run.HostFileExists("RESULT.TXT").Should().BeTrue(
                    "TOOL.BAT in PATH directory should be found and executed")));
    }

    /// <summary>MOVE renames a file inside its directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MoveRenamesFileInSameDirectory() {
        return Scenario("batch_move_rename", run => run
            .Text("SRC.TXT", "hello")
            .StartBatch("MOVE SRC.TXT DST.TXT\r\n", _ => {
                run.HostFileExists("SRC.TXT").Should().BeFalse("MOVE should remove the source file");
                run.TextFile("DST.TXT").Should().Be("hello", "MOVE should create destination with source content");
            }));
    }

    /// <summary>MOVE places a file inside a directory when the destination is a directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MoveFileIntoSubdirectory() {
        return Scenario("batch_move_to_directory", run => run
            .Text("DATA.TXT", "payload")
            .Folder("SUB")
            .StartBatch("MOVE DATA.TXT SUB\r\n", _ => {
                run.HostFileExists("DATA.TXT").Should().BeFalse("MOVE should remove the source file");
                run.TextFile("SUB/DATA.TXT").Should().Be("payload", "MOVE should place the file in the target directory");
            }));
    }

    /// <summary>MOVE with a wildcard moves every matching file into the target directory.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MoveWildcardIntoDirectory() {
        return Scenario("batch_move_wildcard", run => run
            .Text("A.TXT", "aaa")
            .Text("B.TXT", "bbb")
            .Text("KEEP.DAT", "keep")
            .Folder("DEST")
            .StartBatch("MOVE *.TXT DEST\r\n", _ => {
                run.HostFileExists("A.TXT").Should().BeFalse("source A.TXT should be removed");
                run.HostFileExists("B.TXT").Should().BeFalse("source B.TXT should be removed");
                run.HostFileExists("KEEP.DAT").Should().BeTrue("non-matching file should remain");
                run.TextFile("DEST/A.TXT").Should().Be("aaa");
                run.TextFile("DEST/B.TXT").Should().Be("bbb");
            }));
    }

    /// <summary>MOVE of a missing file reports the error and the batch continues.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario MoveOnMissingSourceContinues() {
        return Scenario("batch_move_missing_source", run => run
            .StartBatch("MOVE NOPE.TXT DEST.TXT\r\nECHO OK > DONE.TXT\r\n",
                _ => run.HostFileExists("DONE.TXT").Should().BeTrue(
                    "batch should continue after MOVE fails on a missing file")));
    }

    /// <summary>COPY with a plus sign concatenates two files.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CopyConcatenatesTwoFiles() {
        return Scenario("batch_copy_concat_two", run => run
            .Text("A.TXT", "Hello")
            .Text("B.TXT", "World")
            .StartBatch("COPY A.TXT+B.TXT COMBO.TXT\r\n",
                _ => run.TextFile("COMBO.TXT").Should().Be("HelloWorld",
                    "COPY with + should concatenate file contents")));
    }

    /// <summary>COPY with plus signs concatenates three files in order.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario CopyConcatenatesThreeFiles() {
        return Scenario("batch_copy_concat_three", run => run
            .Text("X.TXT", "One")
            .Text("Y.TXT", "Two")
            .Text("Z.TXT", "Three")
            .StartBatch("COPY X.TXT+Y.TXT+Z.TXT ALL.TXT\r\n",
                _ => run.TextFile("ALL.TXT").Should().Be("OneTwoThree",
                    "COPY with + should concatenate all source files in order")));
    }

    /// <summary>Creating a file through a redirection resolves the parent directory case-insensitively,
    /// so a DOS path of SUB writes into the host directory named Sub.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario RedirectCreatesFileInCaseMismatchedSubdirectory() {
        return Scenario("batch_redirect_case_mismatched_subdir", run => run
            .Folder("Sub")
            .StartBatch("ECHO hello > SUB\\OUT.TXT\r\n", _ => {
                run.HostFileExists("Sub/OUT.TXT").Should().BeTrue(
                    "CreateFileUsingHandle must resolve directory case-insensitively");
                run.TextFile("Sub/OUT.TXT").Trim().Should().Be("hello");
            }));
    }

    /// <summary>DELETE removes a file, as a registered alias of DEL.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario DeleteIsAliasForDel() {
        return Scenario("batch_delete_alias", run => run
            .Text("VICTIM.TXT", "bye")
            .StartBatch("DELETE VICTIM.TXT\r\n",
                _ => run.HostFileExists("VICTIM.TXT").Should().BeFalse(
                    "DELETE should work as an alias for DEL")));
    }

    /// <summary>The jillep.bat of the Jill of the Jungle GOG edition: a CHOICE with mixed switches and
    /// prompt text dispatches to the labeled EXE stub of the selected game.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario JillOfTheJungleChoiceDispatchesToExeStub() {
        return Scenario("batch_jill_choice_dispatches_exe", run => run
            .Binary("JILL1.EXE", BuildVideoWriterCom('1', 0))
            .Binary("JILL2.EXE", BuildVideoWriterCom('2', 0))
            .Binary("JILL3.EXE", BuildVideoWriterCom('3', 0))
            .Text("KEY.TXT", "3")
            .Text("JILLEP.BAT", JillOfTheJungleScript)
            .Run("JILLEP.BAT",
                result => Cell(result, 0).Should().Be('3')));
    }

    /// <summary>CHOICE /c without a colon parses its choice keys, as real world batch files use it.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceWithoutColonParsesChoiceKeys() {
        return Scenario("batch_choice_without_colon", run => run
            .Binary("W2.COM", BuildVideoWriterCom('2', 0))
            .Text("KEY.TXT", "2")
            .StartBatch("CHOICE /c123 < KEY.TXT\r\nIF ERRORLEVEL 2 CALL W2.COM\r\n",
                result => Cell(result, 0).Should().Be('2')));
    }

    /// <summary>CHOICE /n after the prompt text still suppresses the default [Y,N]? suffix.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceNFlagAfterTextSuppressesPrompt() {
        return Scenario("batch_choice_n_after_text", run => run
            .Text("KEY.TXT", "Y")
            .StartBatch("CHOICE /C:YN Pick one /n > PROMPT.TXT < KEY.TXT\r\n", _ => {
                string prompt = run.TextFile("PROMPT.TXT");
                prompt.Should().NotContain("[Y,N]?");
                prompt.Should().Contain("Pick one");
            }));
    }

    /// <summary>CHOICE with a preloaded keystroke reads the key from the emulation loop and sets the
    /// ERRORLEVEL the following IF dispatches on.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario ChoiceWithPreloadedKeySetsErrorlevel() {
        return Scenario("batch_choice_preloaded_key", run => run
            .Binary("W1.COM", BuildVideoWriterCom('1', 0))
            .Binary("W2.COM", BuildVideoWriterCom('2', 0))
            .StartBatch(
                "CHOICE /C:AB\r\n" +
                "IF ERRORLEVEL 2 W2.COM\r\n" +
                "IF ERRORLEVEL 1 W1.COM\r\n",
                new ushort[] { 0x3042 },
                result => Cell(result, 0).Should().Be('2')));
    }

    /// <summary>PAUSE with a preloaded keystroke consumes the key and the batch continues.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario PauseWithPreloadedKeyContinues() {
        return Scenario("batch_pause_preloaded_key", run => run
            .Binary("MARK.COM", BuildVideoWriterCom('P', 0))
            .StartBatch("PAUSE\r\nMARK.COM\r\n",
                new ushort[] { 0x3920 },
                result => Cell(result, 0).Should().Be('P')));
    }

    /// <summary>A host requested .EXE without an MZ header loads as a flat binary, and the bootstrap still
    /// routes it through the .EXE path of AUTOEXEC.BAT.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostRequestedExeExecutesThroughAutoexec() {
        return Scenario("bootstrap_host_requested_exe", run => run
            .Binary("GAME.EXE", BuildVideoWriterCom('X', 0))
            .Run("GAME.EXE",
                result => Cell(result, 0).Should().Be('X')));
    }

    /// <summary>A host requested program living in a subdirectory is routed through AUTOEXEC.BAT.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostRequestedExeInSubdirectoryExecutesThroughAutoexec() {
        return Scenario("bootstrap_host_requested_exe_subdirectory", run => run
            .BinaryIn("GAMES/DUNE", "DUNE.EXE", BuildVideoWriterCom('D', 0))
            .Run("GAMES/DUNE/DUNE.EXE",
                result => Cell(result, 0).Should().Be('D')));
    }

    /// <summary>A host requested program whose path contains a space is quoted in AUTOEXEC.BAT.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostRequestedProgramWithSpacesInPathIsQuoted() {
        return Scenario("bootstrap_host_requested_path_with_spaces", run => run
            .BinaryIn("MY GAMES", "APP.COM", BuildVideoWriterCom('S', 0))
            .Run("MY GAMES/APP.COM",
                result => Cell(result, 0).Should().Be('S')));
    }

    /// <summary>A host requested batch file is routed with CALL, so the batch engine runs it and returns.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostRequestedBatchUsesCallToRoute() {
        return Scenario("bootstrap_host_requested_batch", run => run
            .Binary("WRITER.COM", BuildVideoWriterCom('B', 0))
            .Text(StartBatchFileName, "CALL WRITER.COM\r\n")
            .Run(StartBatchFileName,
                result => Cell(result, 0).Should().Be('B')));
    }

    /// <summary>A host requested batch path written with forward slashes is normalized in the AUTOEXEC.BAT
    /// CALL line.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostRequestedBatchWithSlashPathNormalizesCallPath() {
        return Scenario("bootstrap_host_requested_batch_slash_path", run => {
            run.Text(StartBatchFileName,
                "Z:\r\n" +
                "TYPE AUTOEXEC.BAT > C:\\RESULT.TXT\r\n");
            string slashPath = run.PathOf(StartBatchFileName).Replace('\\', '/');
            return run.RunCustomSettings(run.SettingsForHostPath(slashPath), _ => {
                string output = run.TextFile("RESULT.TXT");
                output.Should().Contain("CALL C:\\START.BAT");
                output.Should().NotContain("CALL C:/START.BAT");
            });
        });
    }

    /// <summary>Host command line arguments survive the AUTOEXEC.BAT routing and reach the batch file as
    /// its positional parameters.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostArgumentsArePreservedThroughAutoexec() {
        return Scenario("bootstrap_arguments_from_host", run => run
            .Binary("W_YES.COM", BuildVideoWriterCom('Y', 0))
            .Text("ROUTE.BAT", "IF \"%1\"==\"RUN\" CALL W_YES.COM\r\n")
            .RunWithArguments("ROUTE.BAT", "RUN",
                result => Cell(result, 0).Should().Be('Y')));
    }

    /// <summary>The exit code of a program propagates through the AUTOEXEC.BAT CALL chain to the IF
    /// ERRORLEVEL that follows it.</summary>
    /// <returns>The scenario.</returns>
    private static AsmScenario HostErrorlevelPropagatesThroughAutoexecChain() {
        return Scenario("bootstrap_errorlevel_propagates", run => run
            .Binary("EXIT3.COM", BuildExitCodeCom(3))
            .Binary("W_OK.COM", BuildVideoWriterCom('K', 0))
            .Text("CHECK.BAT", "CALL EXIT3.COM\r\nIF ERRORLEVEL 3 CALL W_OK.COM\r\n")
            .Run("CHECK.BAT",
                result => Cell(result, 0).Should().Be('K')));
    }

    private static AsmScenario Scenario(string identity, Func<DosProgramRun, AsmFixture> build) {
        return new AsmScenario(identity, scope => build(new DosProgramRun(scope, identity)), []);
    }

    private static AsmScenario ResourceScenario(string identity, IReadOnlyList<string> dependencies,
        Func<DosProgramRun, AsmFixture> build) {
        return new AsmScenario(identity, scope => build(new DosProgramRun(scope, identity)), dependencies);
    }

    private static string[] OutputLines(string output) {
        return output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}
