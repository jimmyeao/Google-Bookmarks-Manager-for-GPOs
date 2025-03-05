using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class DragAdorner : Adorner
    {
        private readonly UIElement _child;
        private Point _position;
        private bool _insertLine;
        private double _insertY;
        private bool _folderDrop;
        private double _folderX;
        private double _folderY;
        private double _folderWidth;
        private double _folderHeight;

        public DragAdorner(UIElement adornedElement, string draggedItemText) : base(adornedElement)
        {
            _child = new TextBlock { Text = draggedItemText, Background = Brushes.LightGray, Padding = new Thickness(5) };
            IsHitTestVisible = false;
        }

        public void UpdatePosition(double x, double y, bool insertLine = false, double insertY = 0, bool folderDrop = false, double folderX = 0, double folderY = 0, double folderWidth = 0, double folderHeight = 0)
        {
            _position = new Point(x, y);
            _insertLine = insertLine;
            _insertY = insertY;
            _folderDrop = folderDrop;
            _folderX = folderX;
            _folderY = folderY;
            _folderWidth = folderWidth;
            _folderHeight = folderHeight;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (_child != null)
            {
                var transform = new TranslateTransform(_position.X, _position.Y);
                drawingContext.PushTransform(transform);
                var visualBrush = new VisualBrush(_child);
                drawingContext.DrawRectangle(visualBrush, null, new Rect(new Point(0, 0), new Size(_child.RenderSize.Width, _child.RenderSize.Height)));
                drawingContext.Pop();
            }
            if (_insertLine)
            {
                double lineY = _position.Y + _insertY;
                drawingContext.DrawLine(new Pen(Brushes.Black, 2), new Point(_position.X - 20, lineY), new Point(_position.X + 20, lineY));
            }
            if (_folderDrop)
            {
                drawingContext.DrawRectangle(Brushes.LightGreen, new Pen(Brushes.Black, 1), new Rect(_folderX, _folderY, _folderWidth, _folderHeight));
            }
        }
    }
}
