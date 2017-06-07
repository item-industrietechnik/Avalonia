﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Avalonia.Controls.Embedding;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Platform;
using Key = Avalonia.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButton = System.Windows.Input.MouseButton;

namespace Avalonia.Win32.Interop.Wpf
{
    class WpfTopLevelImpl : FrameworkElement, IEmbeddableWindowImpl
    {
        private HwndSource _currentHwndSource;
        private readonly HwndSourceHook _hook;
        private readonly IEmbeddableWindowImpl _ttl;
        private IInputRoot _inputRoot;
        private readonly IEnumerable<object> _surfaces;
        private readonly IMouseDevice _mouse;
        private readonly IKeyboardDevice _keyboard;
        private Size _finalSize;

        public EmbeddableControlRoot ControlRoot { get; }
        internal ImageSource ImageSource { get; set; }

        public class CustomControlRoot : EmbeddableControlRoot
        {
            public override void InvalidateMeasure()
            {
                base.InvalidateMeasure();
                ((FrameworkElement)PlatformImpl)?.InvalidateMeasure();
            }
        }

        public WpfTopLevelImpl()
        {
            PresentationSource.AddSourceChangedHandler(this, OnSourceChanged);
            _hook = WndProc;
            _ttl = this;
            _surfaces = new object[] {new WritableBitmapSurface(this)};
            _mouse = new WpfMouseDevice(this);
            _keyboard = AvaloniaLocator.Current.GetService<IKeyboardDevice>();

            ControlRoot = new EmbeddableControlRoot(this);
            SnapsToDevicePixels = true;
            Focusable = true;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (msg == (int)UnmanagedMethods.WindowsMessage.WM_DPICHANGED)
                _ttl.ScalingChanged?.Invoke(_ttl.Scaling);
            return IntPtr.Zero;
        }

        private void OnSourceChanged(object sender, SourceChangedEventArgs e)
        {
            _currentHwndSource?.RemoveHook(_hook);
            _currentHwndSource = e.NewSource as HwndSource;
            _currentHwndSource?.AddHook(_hook);
            _ttl.ScalingChanged?.Invoke(_ttl.Scaling);
        }

        public void Dispose() => _ttl.Closed?.Invoke();

        Size ITopLevelImpl.ClientSize => _finalSize;
        IMouseDevice ITopLevelImpl.MouseDevice => _mouse;

        double ITopLevelImpl.Scaling => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;

        IEnumerable<object> ITopLevelImpl.Surfaces => _surfaces;

        protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
        {
            _finalSize = finalSize.ToAvaloniaSize();
            _ttl.Resized?.Invoke(finalSize.ToAvaloniaSize());
            return base.ArrangeOverride(finalSize);
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) 
            => ControlRoot.MeasureBase(availableSize.ToAvaloniaSize()).ToWpfSize();

        protected override void OnRender(DrawingContext drawingContext)
        {
            _ttl.Paint?.Invoke(new Rect(0, 0, ActualWidth, ActualHeight));
            if (ImageSource != null)
                drawingContext.DrawImage(ImageSource, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
        }

        void ITopLevelImpl.Invalidate(Rect rect) => InvalidateVisual();

        void ITopLevelImpl.SetInputRoot(IInputRoot inputRoot) => _inputRoot = inputRoot;

        Point ITopLevelImpl.PointToClient(Point point) => PointFromScreen(point.ToWpfPoint()).ToAvaloniaPoint();

        Point ITopLevelImpl.PointToScreen(Point point) => PointToScreen(point.ToWpfPoint()).ToAvaloniaPoint();

        protected override void OnLostFocus(RoutedEventArgs e) => LostFocus?.Invoke();


        InputModifiers GetModifiers()
        {
            var state = Keyboard.Modifiers;
            var rv = default(InputModifiers);
            if (state.HasFlag(ModifierKeys.Windows))
                rv |= InputModifiers.Windows;
            if (state.HasFlag(ModifierKeys.Alt))
                rv |= InputModifiers.Alt;
            if (state.HasFlag(ModifierKeys.Control))
                rv |= InputModifiers.Control;
            if (state.HasFlag(ModifierKeys.Shift))
                rv |= InputModifiers.Shift;
            //TODO: mouse modifiers


            return rv;
        }

        void MouseEvent(RawMouseEventType type, MouseEventArgs e)
            => _ttl.Input?.Invoke(new RawMouseEventArgs(_mouse, (uint)e.Timestamp, _inputRoot, type,
            e.GetPosition(this).ToAvaloniaPoint(), GetModifiers()));

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            RawMouseEventType type;
            if(e.ChangedButton == MouseButton.Left)
                type = RawMouseEventType.LeftButtonDown;
            else if (e.ChangedButton == MouseButton.Middle)
                type = RawMouseEventType.MiddleButtonDown;
            else if (e.ChangedButton == MouseButton.Right)
                type = RawMouseEventType.RightButtonDown;
            else
                return;
            MouseEvent(type, e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            RawMouseEventType type;
            if (e.ChangedButton == MouseButton.Left)
                type = RawMouseEventType.LeftButtonUp;
            else if (e.ChangedButton == MouseButton.Middle)
                type = RawMouseEventType.MiddleButtonUp;
            else if (e.ChangedButton == MouseButton.Right)
                type = RawMouseEventType.RightButtonUp;
            else
                return;
            MouseEvent(type, e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            MouseEvent(RawMouseEventType.Move, e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e) =>
            _ttl.Input?.Invoke(new RawMouseWheelEventArgs(_mouse, (uint) e.Timestamp, _inputRoot,
                e.GetPosition(this).ToAvaloniaPoint(), new Vector(0, e.Delta), GetModifiers()));

        protected override void OnMouseLeave(MouseEventArgs e) => MouseEvent(RawMouseEventType.LeaveWindow, e);

        protected override void OnKeyDown(KeyEventArgs e)
            => _ttl.Input?.Invoke(new RawKeyEventArgs(_keyboard, (uint) e.Timestamp, RawKeyEventType.KeyDown,
                (Key) e.Key,
                GetModifiers()));

        protected override void OnKeyUp(KeyEventArgs e)
            => _ttl.Input?.Invoke(new RawKeyEventArgs(_keyboard, (uint)e.Timestamp, RawKeyEventType.KeyUp,
                (Key)e.Key,
                GetModifiers()));

        protected override void OnTextInput(TextCompositionEventArgs e) 
            => _ttl.Input?.Invoke(new RawTextInputEventArgs(_keyboard, (uint) e.Timestamp, e.Text));

        void ITopLevelImpl.SetCursor(IPlatformHandle cursor)
        {
            if (cursor == null)
                Cursor = Cursors.Arrow;
            else if (cursor.HandleDescriptor == "HCURSOR")
                Cursor = CursorShim.FromHCursor(cursor.Handle);
        }

        Action<RawInputEventArgs> ITopLevelImpl.Input { get; set; } //TODO
        Action<Rect> ITopLevelImpl.Paint { get; set; }
        Action<Size> ITopLevelImpl.Resized { get; set; }
        Action<double> ITopLevelImpl.ScalingChanged { get; set; }
        Action ITopLevelImpl.Closed { get; set; }
        public new event Action LostFocus;
        
    }
}
