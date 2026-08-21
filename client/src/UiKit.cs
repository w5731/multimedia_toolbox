using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultimediaClient
{
    /// <summary>通用小部件工厂:对话框式窗口共用样式</summary>
    internal static class UiKit
    {
        internal static readonly FontFamily Font = new FontFamily("Microsoft YaHei");
        internal static readonly SolidColorBrush Dark = new SolidColorBrush(Color.FromRgb(22, 33, 62));
        internal static readonly SolidColorBrush Gray = new SolidColorBrush(Color.FromRgb(120, 120, 120));

        internal static Window MakeDialog(string title, double width, double height)
        {
            Window w = new Window();
            w.Title = title;
            w.Width = width;
            w.Height = height;
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ResizeMode = ResizeMode.NoResize;
            w.FontFamily = Font;
            w.FontSize = 14;
            w.Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));
            return w;
        }

        internal static TextBlock Label(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = Font;
            t.FontSize = 14;
            t.Foreground = Gray;
            t.Margin = new Thickness(0, 10, 0, 4);
            return t;
        }

        internal static TextBox Input(string text)
        {
            TextBox tb = new TextBox();
            tb.Text = text ?? "";
            tb.FontFamily = Font;
            tb.FontSize = 15;
            tb.Padding = new Thickness(8, 6, 8, 6);
            return tb;
        }

        internal static Button PrimaryButton(string text)
        {
            Button b = new Button();
            b.Content = text;
            b.FontFamily = Font;
            b.FontSize = 15;
            b.FontWeight = FontWeights.Bold;
            b.Foreground = Brushes.White;
            b.Background = Dark;
            b.BorderThickness = new Thickness(0);
            b.Padding = new Thickness(22, 8, 22, 8);
            b.Margin = new Thickness(8, 16, 0, 0);
            b.Cursor = System.Windows.Input.Cursors.Hand;
            return b;
        }

        internal static TextBlock ErrorText()
        {
            TextBlock t = new TextBlock();
            t.FontFamily = Font;
            t.FontSize = 13;
            t.Foreground = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 8, 0, 0);
            return t;
        }
    }
}
