namespace CodexVsix.Models;

public class SelectionOption
{
    public SelectionOption(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }

    public override string ToString() => Label;
}
