using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace HourReport.Api.Services;

/// <summary>
/// Ghi file .xlsx có cấu trúc giống hệt file mẫu Hour Report (do Apache POI tạo):
/// XML namespace mặc định (không có tiền tố "x:"), UTF-8 không BOM, đường dẫn quan hệ tương đối,
/// có docProps/core.xml, không có theme. Trình xem trước của GroupWare (Hanbiro) chỉ đọc được dạng này;
/// file do ClosedXML/OpenXML SDK ghi (tiền tố "x:", BOM, đường dẫn tuyệt đối) không xem trước được.
/// </summary>
public static class XlsxTemplateWriter
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    // Chỉ số style trong styles.xml bên dưới.
    public const int StyleCenter = 1; // căn giữa ngang + dọc, xuống dòng, khung mảnh
    public const int StyleLeft = 2;   // căn trái, xuống dòng, khung mảnh

    /// <param name="sheetName">Tên sheet, vd "Sheet 1".</param>
    /// <param name="widths">Độ rộng từng cột (đơn vị ký tự, như trong Excel).</param>
    /// <param name="rows">Mỗi ô: (giá trị text, style).</param>
    public static byte[] Write(string sheetName, IReadOnlyList<double> widths,
        IReadOnlyList<IReadOnlyList<(string Text, int Style)>> rows, DateTime createdUtc)
    {
        // Shared strings: mọi ô lưu kiểu Text, kể cả ô trống (giống file mẫu).
        var sst = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        int Sst(string s)
        {
            total++;
            if (!index.TryGetValue(s, out var i)) { i = sst.Count; sst.Add(s); index[s] = i; }
            return i;
        }

        var cols = widths.Count;
        var lastRow = Math.Max(rows.Count, 1);
        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sheet.Append($"<worksheet xmlns=\"{Main}\"><dimension ref=\"A1:{ColName(cols)}{lastRow}\"/>");
        sheet.Append("<sheetViews><sheetView workbookViewId=\"0\" tabSelected=\"true\"/></sheetViews>");
        sheet.Append("<sheetFormatPr defaultRowHeight=\"15.0\"/><cols>");
        for (var c = 0; c < cols; c++)
            sheet.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{widths[c].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"true\"/>");
        sheet.Append("</cols><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Count; c++)
            {
                var (text, style) = rows[r][c];
                sheet.Append($"<c r=\"{ColName(c + 1)}{r + 1}\" s=\"{style}\" t=\"s\"><v>{Sst(text ?? "")}</v></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData><pageMargins bottom=\"0.75\" footer=\"0.3\" header=\"0.3\" left=\"0.7\" right=\"0.7\" top=\"0.75\"/></worksheet>");

        var shared = new StringBuilder();
        shared.Append($"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<sst count=\"{total}\" uniqueCount=\"{sst.Count}\" xmlns=\"{Main}\">");
        foreach (var s in sst)
            shared.Append(s.Length == 0 ? "<si><t/></si>" : $"<si><t xml:space=\"preserve\">{Esc(s)}</t></si>");
        shared.Append("</sst>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Thứ tự entry giống file mẫu, [Content_Types].xml đứng đầu.
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" Extension=\"rels\"/>" +
                "<Default ContentType=\"application/xml\" Extension=\"xml\"/>" +
                "<Override ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\" PartName=\"/docProps/app.xml\"/>" +
                "<Override ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\" PartName=\"/docProps/core.xml\"/>" +
                "<Override ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\" PartName=\"/xl/sharedStrings.xml\"/>" +
                "<Override ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\" PartName=\"/xl/styles.xml\"/>" +
                "<Override ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\" PartName=\"/xl/workbook.xml\"/>" +
                "<Override ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" PartName=\"/xl/worksheets/sheet1.xml\"/></Types>");
            Add(zip, "_rels/.rels",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?><Relationships xmlns=\"{PkgRel}\">" +
                $"<Relationship Id=\"rId1\" Target=\"xl/workbook.xml\" Type=\"{Rel}/officeDocument\"/>" +
                $"<Relationship Id=\"rId2\" Target=\"docProps/app.xml\" Type=\"{Rel}/extended-properties\"/>" +
                "<Relationship Id=\"rId3\" Target=\"docProps/core.xml\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\"/></Relationships>");
            Add(zip, "docProps/app.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Hour Report</Application></Properties>");
            Add(zip, "docProps/core.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{createdUtc:yyyy-MM-ddTHH:mm:ss}Z</dcterms:created><dc:creator>Hour Report</dc:creator></cp:coreProperties>");
            Add(zip, "xl/sharedStrings.xml", shared.ToString());
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, "xl/workbook.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><workbookPr date1904=\"false\"/>" +
                $"<bookViews><workbookView activeTab=\"0\"/></bookViews><sheets><sheet name=\"{Esc(sheetName)}\" r:id=\"rId3\" sheetId=\"1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?><Relationships xmlns=\"{PkgRel}\">" +
                $"<Relationship Id=\"rId1\" Target=\"sharedStrings.xml\" Type=\"{Rel}/sharedStrings\"/>" +
                $"<Relationship Id=\"rId2\" Target=\"styles.xml\" Type=\"{Rel}/styles\"/>" +
                $"<Relationship Id=\"rId3\" Target=\"worksheets/sheet1.xml\" Type=\"{Rel}/worksheet\"/></Relationships>");
            Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    // Arial 11, khung mảnh 4 cạnh; xf 1 = căn giữa ngang + dọc, xf 2 = căn trái; cả hai đều xuống dòng.
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<styleSheet xmlns=\"" + Main + "\"><numFmts count=\"0\"/>" +
        "<fonts count=\"2\"><font><sz val=\"11.0\"/><color indexed=\"8\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>" +
        "<font><name val=\"Arial\"/><sz val=\"11.0\"/></font></fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"darkGray\"/></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left style=\"thin\"/><right style=\"thin\"/><top style=\"thin\"/><bottom style=\"thin\"/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"true\" applyBorder=\"true\" applyAlignment=\"true\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"true\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"true\" applyAlignment=\"true\" applyBorder=\"true\"><alignment horizontal=\"left\" wrapText=\"true\"/></xf></cellXfs></styleSheet>";

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), Utf8NoBom);
        w.Write(content);
    }

    /// <summary>Escape XML; bỏ ký tự điều khiển không hợp lệ trong XML (giữ tab, xuống dòng).</summary>
    private static string Esc(string s)
    {
        var clean = new string(s.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray());
        return SecurityElement.Escape(clean) ?? "";
    }

    private static string ColName(int col)
    {
        var name = "";
        for (; col > 0; col = (col - 1) / 26) name = (char)('A' + (col - 1) % 26) + name;
        return name;
    }
}
