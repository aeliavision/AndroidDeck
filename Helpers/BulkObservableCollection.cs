using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace VcfEditor.Helpers
{
    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> that supports bulk operations without
    /// firing a <see cref="INotifyCollectionChanged.CollectionChanged"/> event for every
    /// individual item.
    ///
    /// <see cref="ReplaceAll"/> clears the collection and adds all new items in a single
    /// internal operation, then raises exactly ONE <see cref="NotifyCollectionChangedAction.Reset"/>
    /// notification — giving WPF a single layout pass instead of N passes.
    ///
    /// This is the standard WPF pattern for populating large collections without UI lag.
    /// </summary>
    public sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotifications;

        /// <summary>
        /// Replace the entire collection contents with <paramref name="items"/> in one
        /// operation. Raises a single <see cref="NotifyCollectionChangedAction.Reset"/>
        /// event after all items are added.
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            _suppressNotifications = true;
            try
            {
                Items.Clear();
                foreach (var item in items)
                    Items.Add(item);
            }
            finally
            {
                _suppressNotifications = false;
            }

            // Single Reset → one WPF layout pass for all N items.
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        }

        /// <summary>
        /// Update a single item in-place without triggering a full collection reset.
        /// More efficient than <c>collection[index] = item</c> when only one item changes.
        /// </summary>
        public void ReplaceItem(int index, T newItem)
        {
            Items[index] = newItem;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, newItem, index));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotifications)
                base.OnCollectionChanged(e);
        }
    }
}
