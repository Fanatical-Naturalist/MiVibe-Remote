using System.ComponentModel;
using System.Runtime.InteropServices;
using MiVibe.Remote.GattProbe;
using Input = MiVibe.Remote.GattProbe.TypelessShortcut.Input;

internal static class NavigationRegression
{
    private const ushort Control = 0x1D;
    private const uint ScanCode = 0x0008;
    private const uint Extended = 0x0001;
    private const uint KeyUp = 0x0002;
    private const int SimulatedError = 5;
    private static int nativeCallAttempts;
    private static int passed;
    private static int failed;

    private static int Main()
    {
        // The linked production class lives in this assembly. A forgotten sender
        // injection must fail before any user32/kernel32 P/Invoke can execute.
        NativeLibrary.SetDllImportResolver(typeof(TypelessShortcut).Assembly,
            (library, _, _) =>
            {
                Interlocked.Increment(ref nativeCallAttempts);
                throw new InvalidOperationException(
                    $"Native call forbidden in offline navigation tests: {library}");
            });

        Run("x64 INPUT layout", () => Check(Marshal.SizeOf<Input>() == 40,
            "The native x64 INPUT layout must remain 40 bytes."));

        foreach (bool previous in new[] { true, false })
        {
            string direction = previous ? "previous" : "next";
            for (uint accepted = 0; accepted <= 4; accepted++)
            {
                uint prefix = accepted;
                Run($"{direction}: accepted prefix {prefix}/4", () => TestPrefix(previous, prefix));
            }

            Run($"{direction}: release retries then succeeds", () => TestRetry(previous));
            Run($"{direction}: failed Page release still releases Ctrl",
                () => TestReleaseFailure(previous, allKeysFail: false));
            Run($"{direction}: all release failures remain bounded",
                () => TestReleaseFailure(previous, allKeysFail: true));
            Run($"{direction}: cancellation before submission sends nothing",
                () => TestAlreadyCancelled(previous));
            Run($"{direction}: public navigation cancellation precedes native guards",
                () => TestPublicCancellation(previous));

            for (uint accepted = 1; accepted <= 3; accepted++)
            {
                uint prefix = accepted;
                Run($"{direction}: cancellation after prefix {prefix} still cleans up",
                    () => TestCancelledDuringSubmission(previous, prefix));
            }
        }

        Console.WriteLine($"Navigation regression: {passed} passed, {failed} failed; " +
            $"native call attempts: {nativeCallAttempts}; desktop input was not permitted.");
        return failed == 0 && nativeCallAttempts == 0 ? 0 : 1;
    }

    private static void TestPrefix(bool previous, uint accepted)
    {
        var calls = new List<Input[]>();
        uint Sender(Input[] inputs)
        {
            calls.Add(inputs.ToArray());
            Marshal.SetLastPInvokeError(SimulatedError);
            return calls.Count == 1 ? accepted : 1;
        }

        if (accepted == 4)
        {
            TypelessShortcut.SendCodexNavigation(previous, CancellationToken.None, Sender);
        }
        else
        {
            ExpectThrows<Win32Exception>(() =>
                TypelessShortcut.SendCodexNavigation(previous, CancellationToken.None, Sender));
        }

        CheckBatch(calls[0], previous);
        CheckReleases(calls.Skip(1), previous, PendingReleases(previous, accepted));
    }

    private static void TestRetry(bool previous)
    {
        var calls = new List<Input[]>();
        uint Sender(Input[] inputs)
        {
            calls.Add(inputs.ToArray());
            Marshal.SetLastPInvokeError(SimulatedError);
            return calls.Count switch { 1 => 2, 2 or 3 => 0, _ => 1 };
        }

        ExpectThrows<Win32Exception>(() =>
            TypelessShortcut.SendCodexNavigation(previous, CancellationToken.None, Sender));
        CheckBatch(calls[0], previous);
        ushort page = Page(previous);
        CheckReleases(calls.Skip(1), previous, [page, page, page, Control]);
    }

    private static void TestReleaseFailure(bool previous, bool allKeysFail)
    {
        var calls = new List<Input[]>();
        uint Sender(Input[] inputs)
        {
            calls.Add(inputs.ToArray());
            Marshal.SetLastPInvokeError(SimulatedError);
            if (calls.Count == 1)
            {
                return 2;
            }

            return allKeysFail || inputs[0].Union.Keyboard.ScanCode == Page(previous) ? 0U : 1U;
        }

        ExpectThrows<Win32Exception>(() =>
            TypelessShortcut.SendCodexNavigation(previous, CancellationToken.None, Sender));
        CheckBatch(calls[0], previous);
        ushort page = Page(previous);
        ushort[] expected = allKeysFail
            ? [page, page, page, Control, Control, Control]
            : [page, page, page, Control];
        CheckReleases(calls.Skip(1), previous, expected);
    }

    private static void TestAlreadyCancelled(bool previous)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int calls = 0;
        ExpectThrows<OperationCanceledException>(() => TypelessShortcut.SendCodexNavigation(
            previous, cancellation.Token, inputs => { calls++; return (uint)inputs.Length; }));
        Check(calls == 0, "A cancelled request must not invoke the sender.");
    }

    private static void TestPublicCancellation(bool previous)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ExpectThrows<OperationCanceledException>(() => TypelessShortcut.NavigateCodexTaskAsync(
            previous, new IntPtr(1), cancellation.Token).GetAwaiter().GetResult());
    }

    private static void TestCancelledDuringSubmission(bool previous, uint accepted)
    {
        using var cancellation = new CancellationTokenSource();
        var calls = new List<Input[]>();
        uint Sender(Input[] inputs)
        {
            calls.Add(inputs.ToArray());
            Marshal.SetLastPInvokeError(SimulatedError);
            if (calls.Count == 1)
            {
                cancellation.Cancel();
                return accepted;
            }

            return 1;
        }

        ExpectThrows<Win32Exception>(() =>
            TypelessShortcut.SendCodexNavigation(previous, cancellation.Token, Sender));
        CheckBatch(calls[0], previous);
        CheckReleases(calls.Skip(1), previous, PendingReleases(previous, accepted));
    }

    private static void CheckBatch(Input[] batch, bool previous)
    {
        Check(batch.Length == 4, "Navigation must use one complete four-event submission.");
        ushort page = Page(previous);
        ushort[] scans = [Control, page, page, Control];
        uint[] flags = [ScanCode, ScanCode | Extended,
            ScanCode | Extended | KeyUp, ScanCode | KeyUp];
        for (int index = 0; index < scans.Length; index++)
        {
            CheckInput(batch[index], scans[index], flags[index], $"batch event {index}");
        }
    }

    private static void CheckReleases(IEnumerable<Input[]> releases, bool previous, ushort[] expected)
    {
        Input[][] actual = releases.ToArray();
        Check(actual.Length == expected.Length,
            $"Expected {expected.Length} cleanup calls, got {actual.Length}.");
        for (int index = 0; index < expected.Length; index++)
        {
            Check(actual[index].Length == 1, "Each cleanup retry must contain one key-up.");
            uint flags = ScanCode | KeyUp | (expected[index] == Page(previous) ? Extended : 0);
            CheckInput(actual[index][0], expected[index], flags, $"cleanup event {index}");
        }
    }

    private static void CheckInput(Input input, ushort scan, uint flags, string context)
    {
        Check(input.Type == 1, $"{context}: expected keyboard INPUT.");
        Check(input.Union.Keyboard.VirtualKey == 0, $"{context}: expected scan-code input, not VK input.");
        Check(input.Union.Keyboard.ScanCode == scan,
            $"{context}: expected scan 0x{scan:X2}, got 0x{input.Union.Keyboard.ScanCode:X2}.");
        Check(input.Union.Keyboard.Flags == flags,
            $"{context}: expected flags 0x{flags:X}, got 0x{input.Union.Keyboard.Flags:X}.");
        Check(input.Union.Keyboard.Time == 0, $"{context}: expected system-assigned timestamp.");
        Check(input.Union.Keyboard.ExtraInformation.ToUInt64() == 0x4D56544E,
            $"{context}: missing or changed MiVibe injection marker.");
    }

    // Independent expected results; do not duplicate the production prefix formula.
    private static ushort[] PendingReleases(bool previous, uint accepted) => accepted switch
    {
        0 => [],
        1 => [Control],
        2 => [Page(previous), Control],
        3 => [Control],
        4 => [],
        _ => throw new ArgumentOutOfRangeException(nameof(accepted))
    };

    private static ushort Page(bool previous) => previous ? (ushort)0x49 : (ushort)0x51;

    private static void Run(string name, Action test)
    {
        int nativeBefore = nativeCallAttempts;
        try
        {
            test();
            Check(nativeCallAttempts == nativeBefore, "The test attempted an unmocked native call.");
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            failed++;
            Console.Error.WriteLine($"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
