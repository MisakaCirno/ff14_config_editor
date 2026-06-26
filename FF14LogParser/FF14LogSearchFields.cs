namespace FF14LogParser;

[Flags]
public enum FF14LogSearchFields
{
    None = 0,
    Sender = 1,
    Body = 2,
    SenderAndBody = Sender | Body
}
