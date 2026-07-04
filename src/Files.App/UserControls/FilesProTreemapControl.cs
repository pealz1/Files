// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.StorageAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Files.App.UserControls
{
	public sealed partial class FilesProTreemapControl : UserControl
	{
		public static readonly DependencyProperty ItemsSourceProperty =
			DependencyProperty.Register(
				nameof(ItemsSource),
				typeof(IEnumerable<TreemapTileInfo>),
				typeof(FilesProTreemapControl),
				new PropertyMetadata(null, OnItemsSourceChanged));

		private readonly Canvas canvas = new();

		public FilesProTreemapControl()
		{
			Content = canvas;
			SizeChanged += (_, _) => Render();
		}

		public IEnumerable<TreemapTileInfo>? ItemsSource
		{
			get => (IEnumerable<TreemapTileInfo>?)GetValue(ItemsSourceProperty);
			set => SetValue(ItemsSourceProperty, value);
		}

		private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
		{
			if (dependencyObject is FilesProTreemapControl control)
				control.Render();
		}

		private void Render()
		{
			canvas.Children.Clear();

			var items = ItemsSource?.ToArray() ?? [];
			if (items.Length == 0)
				return;

			var widthScale = ActualWidth <= 0 ? 1d : ActualWidth / 1000d;
			var heightScale = ActualHeight <= 0 ? 1d : ActualHeight / 420d;

			for (var index = 0; index < items.Length; index++)
			{
				var tile = items[index];
				var width = Math.Max(1d, tile.Width * widthScale - 2d);
				var height = Math.Max(1d, tile.Height * heightScale - 2d);
				var border = new Border
				{
					Width = width,
					Height = height,
					Background = new SolidColorBrush(GetTileColor(index, tile)),
					BorderBrush = new SolidColorBrush(Color.FromArgb(90, 30, 35, 42)),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(4),
					Padding = new Thickness(6, 4, 6, 4),
					Child = CreateTileContent(tile, width, height)
				};

				ToolTipService.SetToolTip(border, $"{tile.Path}\n{tile.SizeText}");
				Canvas.SetLeft(border, tile.X * widthScale);
				Canvas.SetTop(border, tile.Y * heightScale);
				canvas.Children.Add(border);
			}
		}

		private static UIElement CreateTileContent(TreemapTileInfo tile, double width, double height)
		{
			var panel = new StackPanel
			{
				Spacing = 2
			};

			panel.Children.Add(new TextBlock
			{
				Text = tile.Name,
				TextTrimming = TextTrimming.CharacterEllipsis,
				FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
				FontSize = width < 95d || height < 42d ? 10d : 12d
			});

			if (width >= 80d && height >= 42d)
			{
				panel.Children.Add(new TextBlock
				{
					Text = tile.SizeText,
					TextTrimming = TextTrimming.CharacterEllipsis,
					Opacity = 0.78,
					FontSize = 11d
				});
			}

			return panel;
		}

		private static Color GetTileColor(int index, TreemapTileInfo tile)
		{
			var palette = tile.Kind.Equals("Folder", StringComparison.OrdinalIgnoreCase)
				? FolderPalette
				: FilePalette;

			return palette[index % palette.Length];
		}

		private static readonly Color[] FolderPalette =
		[
			Color.FromArgb(205, 78, 119, 154),
			Color.FromArgb(205, 89, 139, 119),
			Color.FromArgb(205, 128, 111, 158),
			Color.FromArgb(205, 143, 117, 83)
		];

		private static readonly Color[] FilePalette =
		[
			Color.FromArgb(190, 96, 118, 138),
			Color.FromArgb(190, 99, 132, 114),
			Color.FromArgb(190, 133, 105, 139),
			Color.FromArgb(190, 145, 112, 95)
		];
	}
}
