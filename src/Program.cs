using nietras.SeparatedValues;
using System.Diagnostics;
using System.Text;

string kindleExportDirectory;
int year;

if (Debugger.IsAttached)
{
    kindleExportDirectory = @"D:\data\exports\Kindle\";
    year = 2024;
}
else
{
    if (args.Length != 2)
    {
        ShowUsage();
        return;
    }

    kindleExportDirectory = args[0];
    var yearParam = args[1];
    if (!Int32.TryParse(yearParam, out year))
    {
        ShowUsage();
        return;
    }
}


var relation = Path.Combine(kindleExportDirectory, $"{kindleExportDirectory}/Digital.SeriesContent.Relation.2/BookRelation.csv");
var content = Path.Combine(kindleExportDirectory, $"{kindleExportDirectory}/Kindle.KindleContentUpdate/datasets/Kindle.KindleContentUpdate.ContentUpdates/Kindle.KindleContentUpdate.ContentUpdates.csv");
var whisper = Path.Combine(kindleExportDirectory, $"{kindleExportDirectory}/Digital.Content.Whispersync/whispersync.csv");
var metadata = Path.Combine(kindleExportDirectory, $"{kindleExportDirectory}/Kindle.KindleDocs/datasets/Kindle.KindleDocs.DocumentMetadata/Kindle.KindleDocs.DocumentMetadata.csv");
var readingSession = Path.Combine(kindleExportDirectory, @"Kindle.ReadingInsights/datasets/Kindle.reading-insights-sessions_with_adjustments/Kindle.reading-insights-sessions_with_adjustments.csv");

if (!File.Exists(relation) || !File.Exists(content) || !File.Exists(whisper) || !File.Exists(metadata) || !File.Exists(readingSession))
{
    Console.WriteLine("Required Kindle export file does not exist");
    return;
}


const string TemplateName = "template.html";
if (!File.Exists(TemplateName))
{
    Console.WriteLine("Template file does not exist");
    return;
}

var books = new List<(string id, string title)>();
// relation, e.g. B07XNB2XZX
{
    using var reader = Sep.Reader().FromFile(relation);
    foreach (var readRow in reader)
    {
        var id = readRow["ASIN"].ToString();
        var title = readRow["Product Name"].ToString();
        books.Add((id, title));
    }
}

// content
{
    using var reader = Sep.Reader().FromFile(content);
    foreach (var readRow in reader)
    {
        var id = readRow["ASIN"].ToString();
        var title = readRow["\"Product Name\""].ToString();
        if (books.Count(w => w.id == id) == 0)
            books.Add((id, title));
        if (books.First(w => w.id == id).title == "Not Available")
        {
            var f = books.First(w => w.id == id);
            books.Remove(f);
            f.title = title;
            books.Add(f);
        }
    }
}

// whisper
{
    using var reader = Sep.Reader().FromFile(whisper);
    foreach (var readRow in reader)
    {
        var id = readRow["ASIN"].ToString();
        var title = readRow["Product Name"].ToString();
        if (books.Count(w => w.id == id) == 0)
            books.Add((id, title));
        if (books.First(w => w.id == id).title == "Not Available")
        {
            var f = books.First(w => w.id == id);
            books.Remove(f);
            f.title = title;
            books.Add(f);
        }
    }
}

// metadata
{
    using var reader = Sep.Reader().FromFile(metadata);
    foreach (var readRow in reader)
    {
        var id = readRow["DocumentId"].ToString();
        var title = readRow["Title"].ToString();
        if (books.Count(w => w.id == id) == 0)
            books.Add((id, title));
        if (books.First(w => w.id == id).title == "Not Available")
        {
            var f = books.First(w => w.id == id);
            books.Remove(f);
            f.title = title;
            books.Add(f);
        }
    }
}

var notFound = new List<string>();
var readingEvents = new List<(string asin, string start, string end, string totalmilliseconds)>();
// reading session
{
    using var reader = Sep.Reader().FromFile(readingSession);
    foreach (var readRow in reader)
    {
        var id = readRow["ASIN"].ToString();
        var endTime = readRow["end_time"].ToString();
        var startTime = readRow["start_time"].ToString();
        var totalTime = readRow["total_reading_milliseconds"].ToString();

        if (books.Count(w => w.id == id) > 0)
        {
            readingEvents.Add((id, startTime, endTime, totalTime));
        }
        else
        {
            notFound.Add(id);
        }
    }
}

var distinct = notFound.Distinct();
foreach (var id in distinct)
{
    Console.WriteLine(id + " UNKNOWN");
}


var filename = $"{year}.html";
File.Copy(TemplateName, filename, true);
var text = File.ReadAllText(filename);
text = text.Replace("{year}", Convert.ToString(year));

var orderedList = books
    .Join(readingEvents, b => b.Item1, re => re.Item1, (b, re) => new { b, re })
    .OrderByDescending(x => x.re.Item2)
    .Select(x => x.b)
    .ToList();
books = orderedList.ToList();

var sb = new StringBuilder();
var readingtime = (Int64)0;
var bookCount = 0;
foreach (var book in books.Distinct())
{
    var totalTime = readingEvents.Where(w => w.asin == book.id && Convert.ToDateTime(w.start) >= new DateTime(year, 1, 1) && Convert.ToDateTime(w.start) < new DateTime(year + 1, 1, 1)).Sum(s => Convert.ToInt32(s.totalmilliseconds));
    if (totalTime > 0)
    {
        bookCount++;
        readingtime += totalTime;
        Console.WriteLine($"{book.title} read for {ToHumanTime(totalTime)} [{book.id}]");

        var cover = $"<img src=\"unknown-asin.png\" alt=\"{book.title} cover\" />";
        if (book.id.Length == 10)
        {
            cover = $"<a href=\"https://www.amazon.co.uk/dp/{book.id}\"><img src=\"https://images.amazon.com/images/P/{book.id}.jpg\" alt=\"{book.title.Replace("\"","")} cover\" /></a>";
        }
        else if (File.Exists($"{book.id}.jpg"))
        {
            cover = $"<img src=\"{book.id}.jpg\" alt=\"{book.title} cover\" />";
        }

        sb.Append($"""
<div class="book">
    {cover}
    <div>
    <span>{book.title}</span>
        <hr>
        <span>⏱️ {ToHumanTime(totalTime)}</span>
    </div>
</div>
""");
    }
}

text = text.Replace("{books}", sb.ToString());
text = text.Replace("{readingtime}", ToHumanTime(readingtime));
text = text.Replace("{bookcount}", Convert.ToString(bookCount));

File.WriteAllText(filename, text);


static string ToHumanTime(Int64 milliseconds)
{
    var t = TimeSpan.FromMilliseconds(milliseconds);    
    if (t.Days > 0)
    {
        return $"{t.Days} days {t.Hours}h {t.Minutes}m";
    }
    if (t.Hours > 0)
    {
        return $"{t.Hours}h {t.Minutes}m";
    }
    else if (t.Minutes > 0)
    {
        return $"{t.Minutes}m";
    }
    else if (t.Seconds > 0)
    {
        return $"{t.Seconds}s";
    }
    else
    {
        return $"{t.Milliseconds}ms";
    }
}

static void ShowUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  kindleunwrapped <kindle_export_directory> year");
    Console.WriteLine("e.g.");
    Console.WriteLine("  kindleunwrapped D:\\kindledata 2025");
}