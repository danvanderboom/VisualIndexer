using System;
using System.Collections.Generic;
using SkiaSharp;

namespace VisualIndexer;

public class Indexer
{
    public int MaximumGridHeight { get; set; } = 1024;
    public int MaximumGridWidth { get; set; } = 1792;

    public int ColumnIdentifierHeight { get; set; } = 50;
    public int RowIdentifierWidth { get; set; } = 50;

    public GridLayout CalculateGridLayout(int totalCells, int fullImageWidth, int fullImageHeight)
    {
        // Calculate available dimensions for cells only, accounting for identifiers
        int availableWidth = MaximumGridWidth - RowIdentifierWidth;
        int availableHeight = MaximumGridHeight - ColumnIdentifierHeight;

        // Initialize variables for the optimal layout
        int optimalRows = 0;
        int optimalColumns = 0;
        double optimalCellWidth = 0;
        double optimalCellHeight = 0;

        // The best size is determined by the area of the cells
        double bestCellArea = 0;

        // The desired aspect ratio based on the full image dimensions
        double desiredAspectRatio = (double)fullImageWidth / fullImageHeight;

        // Try different configurations to find the best layout
        for (int rows = 1; rows <= totalCells; rows++)
        {
            int columns = (int)Math.Ceiling((double)totalCells / rows);

            // Determine the maximum possible cell width and height based on the current row and column
            double cellWidth = Math.Min(availableWidth / columns, (availableHeight / rows) * desiredAspectRatio);
            double cellHeight = cellWidth / desiredAspectRatio;

            // Ensure the cell height does not exceed the available height per row
            if (cellHeight > availableHeight / rows)
            {
                cellHeight = availableHeight / rows;
                cellWidth = cellHeight * desiredAspectRatio;
            }

            double cellArea = cellWidth * cellHeight;

            // Choose the configuration where the cells are the largest
            if (cellArea > bestCellArea)
            {
                bestCellArea = cellArea;
                optimalRows = rows;
                optimalColumns = columns;
                optimalCellWidth = cellWidth;
                optimalCellHeight = cellHeight;
            }
        }

        // Ensure the cell dimensions are integers
        int thumbnailWidth = (int)optimalCellWidth;
        int thumbnailHeight = (int)optimalCellHeight;

        return new GridLayout
        {
            Rows = optimalRows,
            Columns = optimalColumns,
            CellWidth = thumbnailWidth,
            CellHeight = thumbnailHeight
        };
    }

    public SKBitmap CreatePageGridImage(GridLayout grid, List<SKBitmap> images)
    {
        int imageWidth = grid.Columns * grid.CellWidth + RowIdentifierWidth;
        int imageHeight = grid.Rows * grid.CellHeight + ColumnIdentifierHeight;

        var bitmap = new SKBitmap(imageWidth, imageHeight);
        using (var canvas = new SKCanvas(bitmap))
        {
            // Background color
            canvas.Clear(SKColors.White);

            // Create paint for grid lines
            var linePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Black,
                StrokeWidth = 2
            };

            // Create paint for text
            var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                TextAlign = SKTextAlign.Center,
                TextSize = 20,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };

            // insert images in grid
            for (int i = 0; i < images.Count; i++)
            {
                int row = i / grid.Columns;
                int col = i % grid.Columns;
                var destRect = new SKRect(
                    col * grid.CellWidth + RowIdentifierWidth,
                    row * grid.CellHeight + ColumnIdentifierHeight,
                    (col + 1) * grid.CellWidth + RowIdentifierWidth,
                    (row + 1) * grid.CellHeight + ColumnIdentifierHeight);

                // Resize and draw the image
                canvas.DrawBitmap(images[i], images[i].Info.Rect, destRect);
            }

            // draw horizontal lines
            for (int row = 0; row <= grid.Rows; row++)
            {
                int y = row * grid.CellHeight + ColumnIdentifierHeight;
                canvas.DrawLine(0, y, imageWidth, y, linePaint);
            }

            // draw vertical lines
            for (int col = 0; col <= grid.Columns; col++)
            {
                int x = col * grid.CellWidth + RowIdentifierWidth;
                canvas.DrawLine(x, 0, x, imageHeight, linePaint);
            }

            // Draw row numbers and column letters
            for (int row = 1; row <= grid.Rows; row++)
            {
                var text = row.ToString();
                canvas.DrawText(text, RowIdentifierWidth / 2, row * grid.CellHeight + ColumnIdentifierHeight - grid.CellHeight / 2 + textPaint.TextSize / 2, textPaint);
            }

            for (int col = 1; col <= grid.Columns; col++)
            {
                var text = Convert.ToChar('A' + col - 1).ToString();
                canvas.DrawText(text, col * grid.CellWidth + RowIdentifierWidth - grid.CellWidth / 2, ColumnIdentifierHeight - 20, textPaint);
            }
        }

        return bitmap;
    }

    public Dictionary<string, int> CreateGridCellToPageMap(int startPage, int pageCount, int rowCount, int columnCount)
    {
        var cellToPageMap = new Dictionary<string, int>();

        int currentPage = startPage;
        int maxPages = startPage + pageCount - 1; // the last page that will be mapped

        for (int row = 0; row < rowCount; row++)
        {
            for (int col = 0; col < columnCount; col++)
            {
                if (currentPage > maxPages)
                    break; // stop if all pages are mapped

                string cell = $"{(char)('A' + col)}{row + 1}";
                cellToPageMap[cell] = currentPage;
                currentPage++;
            }
        }

        return cellToPageMap;
    }

    public Dictionary<int, string> CreatePageToGridCellMap(int startPage, int pageCount, int rowCount, int columnCount)
    {
        var gridCellToPageMap = CreateGridCellToPageMap(startPage, pageCount, rowCount, columnCount);

        var invertedMap = new Dictionary<int, string>();

        foreach (var entry in gridCellToPageMap)
        {
            if (!invertedMap.ContainsKey(entry.Value))
            {
                invertedMap.Add(entry.Value, entry.Key);
            }
            else
            {
                // Handle the scenario where a page number is already in the map.
                // This could happen if there's an error in input mapping or 
                // if multiple cells are intentionally mapped to the same page.
                // Depending on requirements, this could be an exception or a merge strategy.
                throw new InvalidOperationException($"Duplicate page number detected: {entry.Value}");
            }
        }

        return invertedMap;
    }
}
