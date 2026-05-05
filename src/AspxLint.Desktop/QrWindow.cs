using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AspxLint.Desktop;

public sealed class QrWindow : Window
{
    public QrWindow(string url)
    {
        Title = "ASPX-LINT — Scanner depuis le telephone";
        Width = 380;
        Height = 460;
        Background = new SolidColorBrush(WpfColor.FromRgb(20, 24, 28));
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var stack = new StackPanel { Margin = new Thickness(20) };

        stack.Children.Add(new TextBlock
        {
            Text = url,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(212, 255, 58)),
            FontFamily = new WpfFontFamily("Consolas"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var pngBytes = new PngByteQRCode(data).GetGraphic(10);

        var bitmap = new BitmapImage();
        using (var ms = new MemoryStream(pngBytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
        }

        stack.Children.Add(new WpfImage
        {
            Width = 320,
            Height = 320,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Source = bitmap
        });

        Content = stack;
    }
}
