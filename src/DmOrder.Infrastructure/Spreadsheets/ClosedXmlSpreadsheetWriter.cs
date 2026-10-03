using ClosedXML.Excel;
using DmOrder.Application.Common.Interfaces;

namespace DmOrder.Infrastructure.Spreadsheets;

/// <summary>
/// Writes .xlsx with ClosedXML (MIT).
///
/// A real workbook rather than CSV. CSV would be less code and it opens in Excel, but it loses every
/// type: an order reference like OV-2026-00123 becomes a formula or a date depending on the
/// machine's locale, leading zeros vanish from phone numbers, and the seller gets a file they have
/// to repair before using. A typed cell survives all of that.
/// </summary>
public sealed class ClosedXmlSpreadsheetWriter : ISpreadsheetWriter
{
    public byte[] Write(
        string sheetName,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SafeSheetName(sheetName));

        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFECE6");

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                Write(sheet.Cell(r + 2, c + 1), row[c]);
            }
        }

        if (rows.Count > 0)
        {
            // A table rather than a plain range: the seller gets filter dropdowns on every column
            // without doing anything, which is most of why they asked for a spreadsheet.
            var range = sheet.Range(1, 1, rows.Count + 1, headers.Count);
            range.CreateTable().Theme = XLTableTheme.TableStyleLight1;
        }

        // Frozen header, so scrolling to order 400 does not lose which column is which.
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 200, 8, 48);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Types the cell rather than stringifying it.
    ///
    /// This is the whole reason for writing a workbook: a date that is a date sorts chronologically,
    /// a number that is a number sums, and text that is text keeps its leading zeros. Anything
    /// unrecognised falls back to its string form rather than being dropped.
    /// </summary>
    private static void Write(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;

            case DateTime date:
                cell.Value = date;
                cell.Style.DateFormat.Format = date.TimeOfDay == TimeSpan.Zero
                    ? "yyyy-mm-dd"
                    : "yyyy-mm-dd hh:mm";
                break;

            case decimal number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;

            case int whole:
                cell.Value = whole;
                break;

            case bool flag:
                cell.Value = flag;
                break;

            default:
                /*
                 * Text, explicitly.
                 *
                 * A reference like "OV-2026-00123" or a phone number starting with a zero must not
                 * be reinterpreted as a formula, a date, or a number with its leading zero shaved
                 * off. The "@" number format is Excel's own way of saying "this cell is text".
                 */
                cell.Style.NumberFormat.Format = "@";
                cell.Value = value.ToString();
                break;
        }
    }

    /// <summary>
    /// Excel caps a sheet name at 31 characters and rejects : \ / ? * [ ] entirely.
    ///
    /// A seller's store name reaches this, so it is sanitised rather than trusted - an invalid name
    /// throws when the workbook is saved, which would turn a punctuation mark in someone's shop name
    /// into a broken feature.
    /// </summary>
    private static string SafeSheetName(string name)
    {
        var cleaned = new string([.. name.Where(c => !"\\/?*[]:".Contains(c))]).Trim();

        if (string.IsNullOrWhiteSpace(cleaned)) return "Orders";

        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
