//using CombineQueries.Domain.Aggregates.Translator;
//using CombineQueries.Domain.Aggregates.Translator.types;
//using NUnit.Framework;

//namespace CombineQueries.Domain.Tests;

//public class ArenaTreeRunesTests
//{
//    [Test]
//    public void Root_IsCreatedAtIdZero_WithNoParent()
//    {
//        var tree = new ArenaTreeRunes<char>();

//        Assert.AreEqual(0, tree.Root.Id);
//        Assert.AreEqual(-1, tree.Root.ParentId);
//    }

//    [Test]
//    public void From_CreatesNewChild_WithCorrectParentAndSymbol()
//    {
//        var tree = new ArenaTreeRunes<char>();

//        var child = tree.From(tree.Root, 'a');

//        Assert.AreEqual(tree.Root.Id, child.ParentId);
//        Assert.AreEqual('a', child.ParentSymbol);
//        Assert.AreEqual(child.Id, tree.Root.Next['a']);
//    }

//    [Test]
//    public void From_IsIdempotent_SameParentAndSymbolReturnsSameNode()
//    {
//        var tree = new ArenaTreeRunes<char>();

//        var first = tree.From(tree.Root, 'a');
//        var second = tree.From(tree.Root, 'a');

//        Assert.AreEqual(first.Id, second.Id); // не плодит дубликат при повторном growth
//    }

//    [Test]
//    public void From_DifferentSymbols_CreateDifferentChildren()
//    {
//        var tree = new ArenaTreeRunes<char>();

//        var a = tree.From(tree.Root, 'a');
//        var b = tree.From(tree.Root, 'b');

//        Assert.AreNotEqual(a.Id, b.Id);
//    }

//    [Test]
//    public void Get_ReturnsNodeById()
//    {
//        var tree = new ArenaTreeRunes<char>();
//        var child = tree.From(tree.Root, 'x');

//        Assert.AreSame(child, tree.Get(child.Id));
//    }

//    [Test]
//    public void Preseeded_CreatesOneChildPerAlphabetSymbol_InOrder()
//    {
//        const string alphabet = "abc";
//        var tree = Translator.ATRFrom(alphabet);

//        Assert.AreEqual(1, tree.Root.Next['a']);
//        Assert.AreEqual(2, tree.Root.Next['b']);
//        Assert.AreEqual(3, tree.Root.Next['c']);

//        Assert.AreEqual('a', tree.Get(1).ParentSymbol);
//        Assert.AreEqual('b', tree.Get(2).ParentSymbol);
//        Assert.AreEqual('c', tree.Get(3).ParentSymbol);
//    }

//    [Test]
//    public void ResetNext_ClearsAllChildren()
//    {
//        var tree = new ArenaTreeRunes<char>();
//        tree.From(tree.Root, 'a');
//        tree.From(tree.Root, 'b');

//        Assert.AreEqual(2, tree.Root.Next.Count);

//        tree.Root.ResetNext();

//        Assert.AreEqual(0, tree.Root.Next.Count);
//    }
//}