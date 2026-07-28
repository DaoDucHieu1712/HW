namespace HW.CS.Demos;

/// <summary>
/// A topic groups the runnable demos for one interview file
/// (e.g. "01 — C# &amp; CLR"). Each item is one interview question.
/// </summary>
public interface IDemoTopic
{
    string Title { get; }
    IReadOnlyList<DemoItem> Items { get; }
}

/// <summary>One runnable interview question demo.</summary>
public record DemoItem(string Title, Action Run);
