using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class DragAdorner : Adorner
    {
        private readonly VisualBrush _visualBrush;
        private double _leftOffset;
        private double _topOffset;
        private bool _showDropIndicator;
        private double _dropIndicatorY;
        private bool _highlightFolder;
        private double _highlightX;
        private double _highlightY;
        private double _highlightWidth;
        private double _highlightHeight;

        public DragAdorner(UIElement adornedElement, object dragContent) : base(adornedElement)
        {
            _visualBrush = new VisualBrush(new ContentPresenter { Content = dragContent, Opacity = 0.7 });
        }

        public void UpdatePosition(double left, double top, bool showDropIndicator = false, double dropIndicatorY = 0,
                                   bool highlightFolder = false, double highlightX = 0, double highlightY = 0,
                                   double highlightWidth = 0, double highlightHeight = 0)
        {
            _leftOffset = left;
            _topOffset = top;
            _showDropIndicator = showDropIndicator;
            _dropIndicatorY = dropIndicatorY;
            _highlightFolder = highlightFolder;
            _highlightX = highlightX;
            _highlightY = highlightY;
            _highlightWidth = highlightWidth;
            _highlightHeight = highlightHeight;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(new Point(_leftOffset, _topOffset), AdornedElement.RenderSize);
            drawingContext.DrawRectangle(_visualBrush, null, rect);

            // Draw drop indicator line
            if (_showDropIndicator)
            {
                Pen pen = new Pen(Brushes.Blue, 3) { DashStyle = DashStyles.Dot };
                drawingContext.DrawLine(pen, new Point(0, _dropIndicatorY), new Point(AdornedElement.RenderSize.Width, _dropIndicatorY));
            }

            // Highlight the folder when hovering
            if (_highlightFolder)
            {
                Pen highlightPen = new Pen(Brushes.Orange, 3);
                Rect highlightRect = new Rect(new Point(_highlightX, _highlightY), new Size(_highlightWidth, _highlightHeight));
                drawingContext.DrawRectangle(null, highlightPen, highlightRect);
            }
        }
    }
}
