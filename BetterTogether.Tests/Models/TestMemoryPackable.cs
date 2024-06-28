using MemoryPack;

namespace BetterTogether.Tests.Models
{
    // A test class to use with MemoryPack, must implement IMemoryPackable
    [MemoryPackable]
    public partial class TestMemoryPackable : IMemoryPackable<TestMemoryPackable>
    {
        public string Value { get; set; }
    }
}
