using MessagePack;

namespace Core.Entities;

[MessagePackObject(true)]
public sealed class HeadersView
{
    public string Key { get; set; }
    
    public string Value { get; set; }
}