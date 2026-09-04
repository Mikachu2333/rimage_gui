using System;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;

namespace RimageGui.Tests
{
    [TestClass]
    public class RangeObservableCollectionSpecs
    {
        private sealed class RecordedChanges
        {
            public int Adds;
            public int Resets;
            public int Removes;

            public void OnChanged(object sender, NotifyCollectionChangedEventArgs e)
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add: Adds++;
                        break;
                    case NotifyCollectionChangedAction.Remove: Removes++;
                        break;
                    case NotifyCollectionChangedAction.Reset: Resets++;
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected collection change: " + e.Action);
                }
            }
        }

        [TestMethod]
        public void SmallAddRange_RaisesOneAddPerItem()
        {
            var collection = new RangeObservableCollection<int>();
            var changes = new RecordedChanges();
            collection.CollectionChanged += changes.OnChanged;

            collection.AddRange(Enumerable.Range(0, 5).ToList());

            Assert.AreEqual(5, collection.Count);
            Assert.AreEqual(5, changes.Adds);
            Assert.AreEqual(0, changes.Resets);
        }

        [TestMethod]
        public void LargeAddRange_CollapsesIntoOneReset()
        {
            var collection = new RangeObservableCollection<int>();
            var changes = new RecordedChanges();
            collection.CollectionChanged += changes.OnChanged;

            collection.AddRange(Enumerable.Range(0, 40).ToList());

            Assert.AreEqual(40, collection.Count);
            Assert.AreEqual(0, changes.Adds);
            Assert.AreEqual(1, changes.Resets);
        }

        [TestMethod]
        public void EmptyOrNullAddRange_IsANoOp()
        {
            var collection = new RangeObservableCollection<int>();
            var changes = new RecordedChanges();
            collection.CollectionChanged += changes.OnChanged;

            collection.AddRange(new int[0]);
            collection.AddRange(null);

            Assert.AreEqual(0, collection.Count);
            Assert.AreEqual(0, changes.Adds + changes.Resets);
        }

        [TestMethod]
        public void RemoveRange_DropsOnlyTheDoomed_AndResetsOnce()
        {
            var collection = new RangeObservableCollection<int>();
            collection.AddRange(Enumerable.Range(0, 10).ToList());
            var changes = new RecordedChanges();
            collection.CollectionChanged += changes.OnChanged;

            collection.RemoveRange(new[] { 3, 5, 7 });

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 4, 6, 8, 9 }, collection.ToList());
            Assert.AreEqual(1, changes.Resets);
        }

        [TestMethod]
        public void RemoveRange_WithForeignItems_IsANoOp()
        {
            var collection = new RangeObservableCollection<int>();
            collection.AddRange(Enumerable.Range(0, 10).ToList());
            var changes = new RecordedChanges();
            collection.CollectionChanged += changes.OnChanged;

            collection.RemoveRange(new[] { 99, 100 });
            collection.RemoveRange(null);

            Assert.AreEqual(10, collection.Count);
            Assert.AreEqual(0, changes.Resets);
        }
    }
}
