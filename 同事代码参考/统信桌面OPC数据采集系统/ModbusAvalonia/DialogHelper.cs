using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace ModbusAvalonia
{
    /// <summary>提供简单的消息提示框和确认框辅助方法。</summary>
    public static class DialogHelper
    {
        /// <summary>弹出消息提示框，显示一条消息和一个“确定”按钮。</summary>
        public static void ShowMessage(Window owner, string message, string title = "提示")
        {
            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 20, 20, 10)
            };

            var btnOk = new Button
            {
                Content = "确定",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var window = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Content = new StackPanel { Children = { textBlock, btnOk } }
            };

            btnOk.Click += (s, e) => window.Close();
            _ = window.ShowDialog(owner);
        }

        /// <summary>弹出确认框，通过回调返回用户的选择结果（确定=true，取消=false）。</summary>
        public static void ShowConfirm(Window owner, string message, Action<bool> onResult, string title = "确认")
        {
            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 20, 20, 10)
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var btnYes = new Button { Content = "确定", Width = 80 };
            var btnNo = new Button { Content = "取消", Width = 80 };
            stack.Children.Add(btnYes);
            stack.Children.Add(btnNo);

            var window = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Content = new StackPanel { Children = { textBlock, stack } }
            };

            btnYes.Click += (s, e) => window.Close(true);
            btnNo.Click += (s, e) => window.Close(false);

            window.ShowDialog<bool>(owner).ContinueWith(t => onResult(t.Result));
        }
    }
}
