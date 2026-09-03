using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace RimageGui.Core
{
    /// <summary>
    /// Observable list with a bulk insert path. A folder scan can contribute
    /// thousands of rows at once; raising one notification per item makes WPF
    /// re-run the collection view for every single add, which is what made bulk
    /// imports feel frozen. Large batches therefore collapse into a single Reset.
    /// </summary>
    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>
        /// Below this many new items, per-item notifications are cheaper than a
        /// Reset and — unlike a Reset — they preserve the grid's selection.
        /// </summary>
        private const int ResetThreshold = 32;

        public void AddRange(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            if (items.Count < ResetThreshold)
            {
                foreach (var item in items)
                {
                    Add(item);
                }

                return;
            }

            CheckReentrancy();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            RaiseReset();
        }

        public void RemoveRange(ICollection<T> doomed)
        {
            if (doomed == null || doomed.Count == 0)
            {
                return;
            }

            CheckReentrancy();
            var kept = new List<T>(Items.Count);
            foreach (var item in Items)
            {
                if (!doomed.Contains(item))
                {
                    kept.Add(item);
                }
            }

            if (kept.Count == Items.Count)
            {
                return;
            }

            Items.Clear();
            foreach (var item in kept)
            {
                Items.Add(item);
            }

            RaiseReset();
        }

        private void RaiseReset()
        {
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
