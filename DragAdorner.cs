using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class DragAdorner : Adorner
    {
        private readonly VisualBrush _visualBrush;
        private double _leftOffset;
        private double _topOffset;

        public DragAdorner(UIElement adornedElement, object dragContent) : base(adornedElement)
        {
            var contentPresenter = new ContentPresenter
            {
                Content = dragContent,
                Opacity = 0.7,
                Width = adornedElement.RenderSize.Width
            };

            _visualBrush = new VisualBrush(contentPresenter);
        }

        public void UpdatePosition(double left, double top)
        {
            _leftOffset = left;
            _topOffset = top;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(new Point(_leftOffset, _topOffset), AdornedElement.RenderSize);
            drawingContext.DrawRectangle(_visualBrush, null, rect);
        }
    }
}
