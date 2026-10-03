namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Turns rows into a spreadsheet file.
///
/// A port so the Application layer never references a spreadsheet library: it decides what the data
/// is, and something in Infrastructure decides what a .xlsx looks like. Swapping the writer - or
/// adding CSV beside it - is one class.
/// </summary>
public interface ISpreadsheetWriter
{
    /// <param name="sheetName">
    /// Excel limits this to 31 characters and forbids several punctuation marks, so implementations
    /// must sanitise rather than trust the caller - a seller's store name reaches this.
    /// </param>
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows);
}
