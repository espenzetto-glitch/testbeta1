// C# 15 `union` needs these two runtime types; .NET 11 before Preview 5 (and older) lack them.
// Where the runtime has them the local copy wins and the compiler only warns (CS0436).
#pragma warning disable CS0436
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    internal sealed class UnionAttribute : Attribute { }

    internal interface IUnion { object? Value { get; } }
}
