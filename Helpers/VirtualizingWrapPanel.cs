using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace VcfEditor.Helpers
{
    public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(160d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(160d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        private Size _extent = new(0, 0);
        private Size _viewport = new(0, 0);
        private Point _offset;

        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; } = true;
        public double ExtentHeight => _extent.Height;
        public double ExtentWidth => _extent.Width;
        public double ViewportHeight => _viewport.Height;
        public double ViewportWidth => _viewport.Width;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;
        public ScrollViewer? ScrollOwner { get; set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureScrollInfo(availableSize);

            var itemsControl = ItemsControl.GetItemsOwner(this);
            if (itemsControl == null) return availableSize;

            var itemCount = itemsControl.HasItems ? itemsControl.Items.Count : 0;
            if (itemCount == 0)
            {
                SetExtent(availableSize, new Size(0, 0));
                return availableSize;
            }

            var itemWidth = Math.Max(1, ItemWidth);
            var itemHeight = Math.Max(1, ItemHeight);

            var columns = Math.Max(1, (int)Math.Floor(availableSize.Width / itemWidth));
            var rows = (int)Math.Ceiling(itemCount / (double)columns);

            var extent = new Size(columns * itemWidth, rows * itemHeight);
            SetExtent(availableSize, extent);

            var firstVisibleIndex = GetFirstVisibleIndex(columns);
            var visibleCount = GetVisibleCount(columns);
            var lastVisibleIndex = Math.Min(itemCount - 1, firstVisibleIndex + visibleCount - 1);

            var generator = ItemContainerGenerator as IItemContainerGenerator;
            if (generator == null) return availableSize;

            var startPos = generator.GeneratorPositionFromIndex(firstVisibleIndex);
            var childIndex = (startPos.Offset == 0) ? startPos.Index : startPos.Index + 1;

            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                for (int itemIndex = firstVisibleIndex; itemIndex <= lastVisibleIndex; itemIndex++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                    if (newlyRealized)
                    {
                        if (childIndex >= InternalChildren.Count)
                            AddInternalChild(child);
                        else
                            InsertInternalChild(childIndex, child);

                        generator.PrepareItemContainer(child);
                    }

                    child.Measure(new Size(itemWidth, itemHeight));
                }
            }

            CleanupItems(firstVisibleIndex, lastVisibleIndex);

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            if (itemsControl == null) return finalSize;

            var itemCount = itemsControl.HasItems ? itemsControl.Items.Count : 0;
            if (itemCount == 0) return finalSize;

            var itemWidth = Math.Max(1, ItemWidth);
            var itemHeight = Math.Max(1, ItemHeight);
            var columns = Math.Max(1, (int)Math.Floor(finalSize.Width / itemWidth));

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                if (itemIndex < 0) continue;

                var row = itemIndex / columns;
                var col = itemIndex % columns;

                var x = col * itemWidth - _offset.X;
                var y = row * itemHeight - _offset.Y;

                child.Arrange(new Rect(new Point(x, y), new Size(itemWidth, itemHeight)));
            }

            return finalSize;
        }

        private void CleanupItems(int firstVisibleIndex, int lastVisibleIndex)
        {
            var generator = ItemContainerGenerator as IItemContainerGenerator;
            if (generator == null) return;

            for (int i = InternalChildren.Count - 1; i >= 0; i--)
            {
                var pos = new GeneratorPosition(i, 0);
                var itemIndex = generator.IndexFromGeneratorPosition(pos);
                if (itemIndex < firstVisibleIndex || itemIndex > lastVisibleIndex)
                {
                    generator.Remove(pos, 1);
                    RemoveInternalChildRange(i, 1);
                }
            }
        }

        private int GetFirstVisibleIndex(int columns)
        {
            var row = (int)Math.Floor(_offset.Y / Math.Max(1, ItemHeight));
            return Math.Max(0, row * columns);
        }

        private int GetVisibleCount(int columns)
        {
            var visibleRows = (int)Math.Ceiling(_viewport.Height / Math.Max(1, ItemHeight)) + 1;
            return Math.Max(1, visibleRows * columns);
        }

        private void EnsureScrollInfo(Size availableSize)
        {
            if (double.IsInfinity(availableSize.Width)) availableSize.Width = 0;
            if (double.IsInfinity(availableSize.Height)) availableSize.Height = 0;

            if (_viewport != availableSize)
            {
                _viewport = availableSize;
                ScrollOwner?.InvalidateScrollInfo();
            }
        }

        private void SetExtent(Size viewport, Size extent)
        {
            _extent = extent;
            _viewport = viewport;

            CoerceOffsets();
            ScrollOwner?.InvalidateScrollInfo();
        }

        private void CoerceOffsets()
        {
            var maxX = Math.Max(0, _extent.Width - _viewport.Width);
            var maxY = Math.Max(0, _extent.Height - _viewport.Height);

            _offset = new Point(
                Math.Min(Math.Max(0, _offset.X), maxX),
                Math.Min(Math.Max(0, _offset.Y), maxY));
        }

        public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
        public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - ItemWidth);
        public void LineRight() => SetHorizontalOffset(HorizontalOffset + ItemWidth);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
        public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - ItemHeight * 3);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + ItemHeight * 3);
        public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - ItemWidth * 3);
        public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + ItemWidth * 3);

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            var child = visual as UIElement;
            if (child == null) return rectangle;

            var itemIndex = ((System.Windows.Controls.ItemContainerGenerator)ItemContainerGenerator)
                .IndexFromContainer(child);
            if (itemIndex < 0) return rectangle;

            var viewportWidth = Math.Max(1, _viewport.Width);
            var columns = Math.Max(1, (int)Math.Floor(viewportWidth / Math.Max(1, ItemWidth)));
            var row = itemIndex / columns;

            SetVerticalOffset(row * ItemHeight);

            return rectangle;
        }

        public void SetHorizontalOffset(double offset)
        {
            if (double.IsNaN(offset)) return;
            _offset.X = offset;
            CoerceOffsets();
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        public void SetVerticalOffset(double offset)
        {
            if (double.IsNaN(offset)) return;
            _offset.Y = offset;
            CoerceOffsets();
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }
    }
}
