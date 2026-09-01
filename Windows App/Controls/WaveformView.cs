using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace Voxa.Controls
{
    public class WaveformView : FrameworkElement
    {
        public static readonly DependencyProperty PeaksProperty =
            DependencyProperty.Register(
                nameof(Peaks),
                typeof(IEnumerable),
                typeof(WaveformView),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnPeaksChanged));

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(
                nameof(Progress),
                typeof(double),
                typeof(WaveformView),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    null,
                    CoerceProgress));

        public static readonly DependencyProperty PlayedBrushProperty =
            DependencyProperty.Register(
                nameof(PlayedBrush),
                typeof(Brush),
                typeof(WaveformView),
                new FrameworkPropertyMetadata(
                    Brushes.MediumPurple,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnBrushChanged));

        public static readonly DependencyProperty UnplayedBrushProperty =
            DependencyProperty.Register(
                nameof(UnplayedBrush),
                typeof(Brush),
                typeof(WaveformView),
                new FrameworkPropertyMetadata(
                    Brushes.Gray,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnBrushChanged));

        private double[] _cachedPeaks = Array.Empty<double>();
        private INotifyCollectionChanged? _observedPeaks;
        private double _cachedDrawingWidth;
        private double _cachedDrawingHeight;
        private DrawingGroup? _playedWaveform;
        private DrawingGroup? _unplayedWaveform;

        public IEnumerable? Peaks
        {
            get => (IEnumerable?)GetValue(PeaksProperty);
            set => SetValue(PeaksProperty, value);
        }

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public Brush PlayedBrush
        {
            get => (Brush)GetValue(PlayedBrushProperty);
            set => SetValue(PlayedBrushProperty, value);
        }

        public Brush UnplayedBrush
        {
            get => (Brush)GetValue(UnplayedBrushProperty);
            set => SetValue(UnplayedBrushProperty, value);
        }

        private static void OnPeaksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (WaveformView)d;

            if (view._observedPeaks != null)
                view._observedPeaks.CollectionChanged -= view.OnPeaksCollectionChanged;

            view._observedPeaks = e.NewValue as INotifyCollectionChanged;
            if (view._observedPeaks != null)
                view._observedPeaks.CollectionChanged += view.OnPeaksCollectionChanged;

            view.RefreshPeakCache();
        }

        private static object CoerceProgress(DependencyObject d, object baseValue)
        {
            var progress = baseValue is double value ? value : 0.0;
            if (double.IsNaN(progress) || double.IsInfinity(progress)) progress = 0;
            return Math.Max(0, Math.Min(1, progress));
        }

        private static void OnBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (WaveformView)d;
            view.ClearDrawingCache();
        }

        private void OnPeaksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => RefreshPeakCache();

        private void RefreshPeakCache()
        {
            if (Peaks == null)
            {
                _cachedPeaks = Array.Empty<double>();
                ClearDrawingCache();
                InvalidateVisual();
                return;
            }

            var peaks = new List<double>();
            foreach (var value in Peaks)
            {
                var peak = value is float f ? f : value is double d ? d : 0.0;
                if (double.IsNaN(peak) || double.IsInfinity(peak)) peak = 0;
                peaks.Add(Math.Max(0, Math.Min(1, peak)));
            }

            _cachedPeaks = peaks.ToArray();
            ClearDrawingCache();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            var progress = Progress;
            var playheadX = width * progress;

            if (_cachedPeaks.Length == 0)
            {
                DrawPlayhead(dc, playheadX, height);
                return;
            }

            EnsureDrawingCache(width, height);

            if (_unplayedWaveform != null)
                dc.DrawDrawing(_unplayedWaveform);

            if (_playedWaveform != null && playheadX > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, playheadX, height)));
                dc.DrawDrawing(_playedWaveform);
                dc.Pop();
            }

            DrawPlayhead(dc, playheadX, height);
        }

        private void EnsureDrawingCache(double width, double height)
        {
            if (_playedWaveform != null &&
                _unplayedWaveform != null &&
                Math.Abs(_cachedDrawingWidth - width) < 0.1 &&
                Math.Abs(_cachedDrawingHeight - height) < 0.1)
            {
                return;
            }

            _cachedDrawingWidth = width;
            _cachedDrawingHeight = height;
            _playedWaveform = BuildWaveformDrawing(width, height, PlayedBrush);
            _unplayedWaveform = BuildWaveformDrawing(width, height, UnplayedBrush, opacity: 0.55);
        }

        private DrawingGroup BuildWaveformDrawing(double width, double height, Brush brush, double opacity = 1.0)
        {
            var group = new DrawingGroup { Opacity = opacity };
            using (var context = group.Open())
            {
                var centerY = height / 2.0;
                var gap = 2.0;
                var barWidth = Math.Max(1.5, (width - gap * (_cachedPeaks.Length - 1)) / _cachedPeaks.Length);
                var maxBarHeight = Math.Max(2.0, height - 6.0);

                for (var i = 0; i < _cachedPeaks.Length; i++)
                {
                    var x = i * (barWidth + gap);
                    var barHeight = Math.Max(2.0, _cachedPeaks[i] * maxBarHeight);
                    var rect = new Rect(x, centerY - barHeight / 2.0, barWidth, barHeight);
                    context.DrawRoundedRectangle(brush, null, rect, 1.2, 1.2);
                }
            }

            try { group.Freeze(); } catch { /* Dynamic theme brushes may not always be freezable. */ }
            return group;
        }

        private void ClearDrawingCache()
        {
            _cachedDrawingWidth = 0;
            _cachedDrawingHeight = 0;
            _playedWaveform = null;
            _unplayedWaveform = null;
        }

        private void DrawPlayhead(DrawingContext dc, double x, double height)
        {
            x = Math.Max(0, Math.Min(ActualWidth, x));
            var linePen = new Pen(PlayedBrush, 2.5);
            dc.DrawLine(linePen, new Point(x, 0), new Point(x, height));
            dc.DrawEllipse(Brushes.White, linePen, new Point(x, height / 2.0), 5.5, 5.5);
        }
    }
}
