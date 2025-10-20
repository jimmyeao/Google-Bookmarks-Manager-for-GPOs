using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public enum InsertionPosition
    {
        Above,
        Inside,
        Below
    }

    public class InsertionAdorner : Adorner
    {
        private readonly Pen _linePen;
        private readonly Brush _insideBrush;
        private readonly InsertionPosition _position;

        public InsertionAdorner(UIElement adornedElement, InsertionPosition position) : base(adornedElement)
        {
            _position = position;

            _linePen = new Pen(new SolidColorBrush(Color.FromRgb(33, 150, 243)), 2);
            _linePen.Freeze();

            _insideBrush = new SolidColorBrush(Color.FromArgb(60, 33, 150, 243));
            _insideBrush.Freeze();
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(new Point(0, 0), AdornedElement.RenderSize);

            switch (_position)
            {
                case InsertionPosition.Above:
                    drawingContext.DrawLine(_linePen, new Point(0, 0), new Point(rect.Width, 0));
                    break;
                case InsertionPosition.Below:
                    drawingContext.DrawLine(_linePen, new Point(0, rect.Height), new Point(rect.Width, rect.Height));
                    break;
                case InsertionPosition.Inside:
                    drawingContext.DrawRectangle(_insideBrush, null, rect);
                    break;
            }
        }
    }
}
