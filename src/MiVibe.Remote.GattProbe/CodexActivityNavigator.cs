using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace MiVibe.Remote.GattProbe;

internal enum ActivityNavigationResult
{
    NotActivityView,
    Invoked,
    Boundary,
    Unavailable,
    Skipped
}

/// <summary>
/// Follows the loaded activity-list rows in their UI Automation tree order.
/// Never reads task names, text, URLs, or coordinates and never sends input.
/// </summary>
internal static class CodexActivityNavigator
{
    private const string LeftPanelClass = "app-shell-left-panel";
    private const string ActivityListClass = "@container/priority-list";
    private const string TaskRowClass = "sidebar-item";
    private const string SelectionVariantPrefix = "data-[app-action-sidebar-thread-selected=true]:";
    private const string CurrentClass = "bg-primary-ghost-hover";
    private const int MaximumDepth = 16;
    private const int MaximumNodes = 4096;
    private const int MaximumRows = 256;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(2);
    private static readonly Condition PopupCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
    private static readonly Condition ExpandableCondition = new PropertyCondition(
        AutomationElement.IsExpandCollapsePatternAvailableProperty, true);
    private static int navigating;

    internal static ActivityNavigationResult Navigate(
        bool previous, IntPtr expectedForeground, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref navigating, 1, 0) != 0)
        {
            return ActivityNavigationResult.Skipped;
        }

        try
        {
            var guard = new NavigationGuard(expectedForeground, cancellationToken);
            guard.Check();
            AutomationElement window = AutomationElement.FromHandle(expectedForeground);
            guard.Check();
            if (window is null)
            {
                return ActivityNavigationResult.Unavailable;
            }

            // Locate the app shell's nearest matching panel. Deeper embedded
            // web pages cannot substitute a similarly named descendant.
            ElementView? panel = FindNearestClass(window, LeftPanelClass, guard);
            if (panel is null || panel.IsOffscreen)
            {
                // Missing app-shell accessibility is not evidence of another view.
                return ActivityNavigationResult.Unavailable;
            }

            ElementView? list = FindNearestClass(panel.Element, ActivityListClass, guard);
            if (list is null)
            {
                // A hidden or replaced panel is not evidence that activity view
                // is off. Confirm absence again on the same visible surface.
                ElementView? confirmedPanel = FindNearestClass(window, LeftPanelClass, guard);
                if (confirmedPanel is null || confirmedPanel.IsOffscreen ||
                    !SameIdentity(panel.Element, confirmedPanel.Element, guard))
                {
                    return ActivityNavigationResult.Unavailable;
                }

                guard.Check();
                return FindNearestClass(confirmedPanel.Element, ActivityListClass, guard) is null
                    ? ActivityNavigationResult.NotActivityView
                    : ActivityNavigationResult.Unavailable;
            }

            if (list.ControlType != ControlType.List || panel.IsOffscreen || list.IsOffscreen)
            {
                return ActivityNavigationResult.Unavailable;
            }

            RejectOpenPopups(window, guard);
            Snapshot first = ReadSnapshot(list.Element, guard);
            guard.Check();

            // Reacquire the list and every row instead of trusting objects from
            // before a render, an archive, or a change in the activity groups.
            ElementView? refreshedPanel = FindNearestClass(window, LeftPanelClass, guard);
            if (refreshedPanel is null || refreshedPanel.IsOffscreen ||
                !SameIdentity(panel.Element, refreshedPanel.Element, guard))
            {
                return ActivityNavigationResult.Unavailable;
            }

            ElementView? refreshedList = FindNearestClass(refreshedPanel.Element, ActivityListClass, guard);
            if (refreshedList is null || refreshedList.IsOffscreen ||
                refreshedList.ControlType != ControlType.List ||
                !SameIdentity(list.Element, refreshedList.Element, guard))
            {
                return ActivityNavigationResult.Unavailable;
            }

            RejectOpenPopups(window, guard);
            Snapshot second = ReadSnapshot(refreshedList.Element, guard);
            if (!SameSnapshot(first, second))
            {
                return ActivityNavigationResult.Unavailable;
            }

            int nextIndex = second.CurrentIndex + (previous ? -1 : 1);
            guard.Check();
            if (nextIndex < 0 || nextIndex >= second.Rows.Count)
            {
                return ActivityNavigationResult.Boundary;
            }

            Row current = second.Rows[second.CurrentIndex];
            Row target = second.Rows[nextIndex];
            if (!target.IsEnabled)
            {
                return ActivityNavigationResult.Unavailable;
            }

            RejectOpenPopups(window, guard);
            RejectExpandedRows([current.Element, target.Element], guard);
            // A final pair check rejects a row that was replaced or selected
            // after the second snapshot. Invoke is the sole mutation below.
            if (!MatchesRow(current, mustBeCurrent: true, guard) ||
                !MatchesRow(target, mustBeCurrent: false, guard))
            {
                return ActivityNavigationResult.Unavailable;
            }

            guard.Check();
            if (!target.Element.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern) ||
                pattern is not InvokePattern invoke)
            {
                return ActivityNavigationResult.Unavailable;
            }

            guard.Check();
            invoke.Invoke();
            return ActivityNavigationResult.Invoked;
        }
        catch (OperationCanceledException)
        {
            return ActivityNavigationResult.Skipped;
        }
        catch (Exception)
        {
            // ElementNotAvailable/COM/provider errors are expected during a
            // rerender or app shutdown. No exception text or task data is logged.
            return ActivityNavigationResult.Unavailable;
        }
        finally
        {
            Volatile.Write(ref navigating, 0);
        }
    }

    private static ElementView? FindNearestClass(
        AutomationElement root, string classToken, NavigationGuard guard)
    {
        List<AutomationElement> level = [root];
        for (int depth = 0; level.Count > 0; depth++)
        {
            if (depth >= MaximumDepth)
            {
                throw new InvalidOperationException("Accessibility depth limit.");
            }

            List<ElementView> matches = [];
            List<AutomationElement> next = [];
            foreach (AutomationElement element in level)
            {
                foreach (ElementView child in ReadChildren(element, guard))
                {
                    if (HasClass(child.ClassName, classToken))
                    {
                        matches.Add(child);
                    }
                    else if (IsContainer(child.ControlType))
                    {
                        next.Add(child.Element);
                    }
                }
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException("Ambiguous activity surface.");
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            level = next;
        }

        return null;
    }

    // Read-only entry used by the local integration diagnostic via reflection.
    // It exercises the production filters without navigating or changing focus.
    private static Snapshot CaptureSnapshot(
        AutomationElement list, IntPtr expectedForeground, CancellationToken cancellationToken)
    {
        var guard = new NavigationGuard(expectedForeground, cancellationToken);
        guard.Check();
        RejectOpenPopups(AutomationElement.FromHandle(expectedForeground), guard);
        return ReadSnapshot(list, guard);
    }

    private static Snapshot ReadSnapshot(AutomationElement list, NavigationGuard guard)
    {
        List<Row> rows = [];
        var pending = new Stack<(AutomationElement Element, int Depth)>();
        pending.Push((list, 0));
        while (pending.Count > 0)
        {
            (AutomationElement element, int depth) = pending.Pop();
            if (depth == -1)
            {
                ElementView[] candidates = ReadChildren(element, guard)
                    .Where(IsTaskButton).ToArray();
                if (candidates.Length > 1)
                {
                    throw new InvalidOperationException("Ambiguous task row.");
                }

                // Headings, spacers, and Load more are not task rows.
                if (candidates.Length == 0)
                {
                    continue;
                }

                ElementView button = candidates[0];
                rows.Add(new Row(button.Element, Identity(button.Element, guard),
                    HasClass(button.ClassName, CurrentClass), button.IsEnabled));
                if (rows.Count > MaximumRows)
                {
                    throw new InvalidOperationException("Activity row limit.");
                }

                continue;
            }

            if (depth >= MaximumDepth)
            {
                throw new InvalidOperationException("Activity depth limit.");
            }

            IReadOnlyList<ElementView> children = ReadChildren(element, guard);
            // Reverse the stack insertion to preserve UIA's document order,
            // including rows outside the current scroll viewport.
            for (int index = children.Count - 1; index >= 0; index--)
            {
                ElementView child = children[index];
                if (child.ControlType == ControlType.ListItem)
                {
                    pending.Push((child.Element, -1));
                }
                else if (IsContainer(child.ControlType))
                {
                    pending.Push((child.Element, depth + 1));
                }
            }
        }

        int[] current = rows.Select((row, index) => (row, index))
            .Where(item => item.row.IsCurrent).Select(item => item.index).ToArray();
        if (current.Length != 1)
        {
            throw new InvalidOperationException("Current activity row is not unique.");
        }

        RejectExpandedRows(rows.Select(row => row.Element), guard);
        return new Snapshot(rows, current[0]);
    }

    private static void RejectExpandedRows(IEnumerable<AutomationElement> rows, NavigationGuard guard)
    {
        foreach (AutomationElement row in rows)
        {
            guard.Check();
            AutomationElementCollection controls = row.FindAll(TreeScope.Subtree, ExpandableCondition);
            guard.Count(controls.Count);
            foreach (AutomationElement control in controls)
            {
                guard.Check();
                if (control.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object pattern) &&
                    pattern is ExpandCollapsePattern expandable &&
                    expandable.Current.ExpandCollapseState is ExpandCollapseState.Expanded or
                        ExpandCollapseState.PartiallyExpanded)
                {
                    throw new InvalidOperationException("Task menu is expanded.");
                }
            }
        }
    }

    private static void RejectOpenPopups(AutomationElement window, NavigationGuard guard)
    {
        guard.Check();
        uint threadId = GetWindowThreadProcessId(guard.Window, out _);
        var threadInfo = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (threadId == 0 || !GetGUIThreadInfo(threadId, ref threadInfo) ||
            (threadInfo.Flags & 0x1C) != 0 || threadInfo.MenuOwner != IntPtr.Zero)
        {
            throw new InvalidOperationException("Native menu state is unavailable or open.");
        }

        guard.Check();
        AutomationElementCollection popups = window.FindAll(TreeScope.Descendants, PopupCondition);
        guard.Count(popups.Count);
        foreach (AutomationElement popup in popups)
        {
            guard.Check();
            if (popup.Current.IsOffscreen)
            {
                continue;
            }

            if (popup.Current.ControlType == ControlType.Menu ||
                popup.TryGetCurrentPattern(WindowPattern.Pattern, out object pattern) &&
                pattern is WindowPattern modal && modal.Current.IsModal)
            {
                throw new InvalidOperationException("App menu or modal dialog is open.");
            }
        }
    }

    private static IReadOnlyList<ElementView> ReadChildren(AutomationElement parent, NavigationGuard guard)
    {
        guard.Check();
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.ClassNameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.IsEnabledProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        AutomationElementCollection children;
        using (cache.Activate())
        {
            children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
        }

        guard.Count(children.Count);
        List<ElementView> result = new(children.Count);
        foreach (AutomationElement child in children)
        {
            guard.Check();
            result.Add(new ElementView(child, child.Cached.ClassName, child.Cached.ControlType,
                child.Cached.IsEnabled, child.Cached.IsOffscreen));
        }

        return result;
    }

    private static bool MatchesRow(Row row, bool mustBeCurrent, NavigationGuard guard)
    {
        guard.Check();
        string className = row.Element.Current.ClassName;
        bool enabled = row.Element.Current.IsEnabled;
        guard.Check();
        return enabled && HasClass(className, TaskRowClass) && HasSelectionVariant(className) &&
            HasClass(className, CurrentClass) == mustBeCurrent &&
            row.RuntimeId.SequenceEqual(Identity(row.Element, guard));
    }

    private static bool SameSnapshot(Snapshot first, Snapshot second) =>
        first.CurrentIndex == second.CurrentIndex && first.Rows.Count == second.Rows.Count &&
        first.Rows.Zip(second.Rows).All(pair =>
            pair.First.IsEnabled == pair.Second.IsEnabled &&
            pair.First.RuntimeId.SequenceEqual(pair.Second.RuntimeId));

    private static bool SameIdentity(AutomationElement first, AutomationElement second, NavigationGuard guard) =>
        Identity(first, guard).SequenceEqual(Identity(second, guard));

    private static int[] Identity(AutomationElement element, NavigationGuard guard)
    {
        guard.Check();
        int[] identity = element.GetRuntimeId();
        guard.Check();
        return identity.Length > 0 ? identity : throw new InvalidOperationException("Missing UIA identity.");
    }

    private static bool IsTaskButton(ElementView view) => view.ControlType == ControlType.Button &&
        HasClass(view.ClassName, TaskRowClass) && HasSelectionVariant(view.ClassName);

    private static bool HasClass(string classes, string token) =>
        classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Contains(token, StringComparer.Ordinal);

    private static bool HasSelectionVariant(string classes) =>
        classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.StartsWith(SelectionVariantPrefix, StringComparison.Ordinal));

    private static bool IsContainer(ControlType type) => type == ControlType.Group ||
        type == ControlType.Pane || type == ControlType.Custom || type == ControlType.Document ||
        type == ControlType.Window || type == ControlType.List;

    private sealed record ElementView(AutomationElement Element, string ClassName,
        ControlType ControlType, bool IsEnabled, bool IsOffscreen);

    private sealed record Row(AutomationElement Element, int[] RuntimeId, bool IsCurrent, bool IsEnabled);

    private sealed record Snapshot(IReadOnlyList<Row> Rows, int CurrentIndex);

    private sealed class NavigationGuard(IntPtr window, CancellationToken cancellationToken)
    {
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private int nodes;
        internal IntPtr Window { get; } = window;

        internal void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Window == IntPtr.Zero || GetForegroundWindow() != Window || TypelessShortcut.AnyModifierIsDown())
            {
                throw new OperationCanceledException("Foreground or modifiers changed.");
            }

            if (elapsed.Elapsed > ReadBudget)
            {
                throw new InvalidOperationException("Accessibility read timed out.");
            }
        }

        internal void Count(int count)
        {
            Check();
            nodes += count;
            if (nodes > MaximumNodes)
            {
                throw new InvalidOperationException("Accessibility node limit.");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
}
