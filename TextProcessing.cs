using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LanguageDetector
{
    // This class reads the text out of a book file and cleans it up so that
    // the analyzers can work with it. It can read .txt, .pdf and .docx files.
    public class TextCleaner
    {
        // Returns true if a character is a letter, including accented ones
        // like š, č, ž, á, é that appear in European languages.
        private bool IsLetter(char ch)
        {
            bool result = char.IsLetter(ch);
            return result;
        }

        // Returns true if the character is an apostrophe. There are two kinds:
        // the straight apostrophe (') and the curly right single quote (’).
        private bool IsApostrophe(char ch)
        {
            if (ch == '\'')
            {
                return true;
            }
            if (ch == '’')
            {
                return true;
            }
            return false;
        }

        // Converts one character to its lower-case version and returns it.
        private char MakeLowerCase(char ch)
        {
            char lower = char.ToLower(ch);
            return lower;
        }

        // Returns true when the string is null or has zero characters.
        private bool IsNullOrEmpty(string text)
        {
            if (text == null)
            {
                return true;
            }
            if (text.Length == 0)
            {
                return true;
            }
            return false;
        }

        // Returns true when the string is not null and has at least one character.
        private bool IsNonEmpty(string text)
        {
            if (text == null)
            {
                return false;
            }
            if (text.Length > 0)
            {
                return true;
            }
            return false;
        }

        // Returns true when a word that came from splitting has actual content.
        private bool WordHasContent(string word)
        {
            bool hasContent = IsNonEmpty(word);
            return hasContent;
        }

        // Gets the file extension from a path and converts it to lower case.
        // For example "C:\books\Story.PDF" gives back ".pdf".
        private string GetFileExtension(string path)
        {
            string ext = Path.GetExtension(path);
            string extLower = ext.ToLower();
            return extLower;
        }

        // Splits a string on space characters and returns only the non-empty parts.
        private List<string> SplitOnSpaces(string text)
        {
            string[] rawParts = text.Split(' ');
            List<string> filteredParts = new List<string>();
            for (int i = 0; i < rawParts.Length; i++)
            {
                string part = rawParts[i];
                bool hasContent = WordHasContent(part);
                if (hasContent)
                {
                    filteredParts.Add(part);
                }
            }
            return filteredParts;
        }

        // Joins a list of words back into one string with a single space between each word.
        private string JoinWithSpaces(List<string> words)
        {
            string result = string.Join(" ", words);
            return result;
        }

        // Reads a file and returns the text inside it. We look at the file
        // extension to decide how to read the file.
        public string LoadFromFile(string path)
        {
            string extLower = GetFileExtension(path);
            if (extLower == ".txt")
            {
                string content = ExtractFromTxt(path);
                return content;
            }
            else if (extLower == ".pdf")
            {
                string content = ExtractFromPdf(path);
                return content;
            }
            else if (extLower == ".docx")
            {
                string content = ExtractFromDocx(path);
                return content;
            }
            else
            {
                string message = "Unsupported file format: " + extLower;
                throw new NotSupportedException(message);
            }
        }

        // Reads a plain text file and returns everything in it.
        private string ExtractFromTxt(string path)
        {
            string content = File.ReadAllText(path, Encoding.UTF8);
            return content;
        }

        // Reads a PDF file page by page and joins all the text together.
        private string ExtractFromPdf(string path)
        {
            StringBuilder builder = new StringBuilder();
            using (PdfDocument document = PdfDocument.Open(path))
            {
                IEnumerable<Page> pages = document.GetPages();
                foreach (Page page in pages)
                {
                    string pageText = page.Text;
                    builder.AppendLine(pageText);
                }
            }
            string result = builder.ToString();
            return result;
        }

        // Reads a Word .docx file and returns all the text in it.
        private string ExtractFromDocx(string path)
        {
            using (WordprocessingDocument document = WordprocessingDocument.Open(path, false))
            {
                bool mainPartMissing = document.MainDocumentPart == null;
                if (mainPartMissing)
                {
                    return "";
                }
                bool documentMissing = document.MainDocumentPart.Document == null;
                if (documentMissing)
                {
                    return "";
                }
                bool bodyMissing = document.MainDocumentPart.Document.Body == null;
                if (bodyMissing)
                {
                    return "";
                }
                string text = document.MainDocumentPart.Document.Body.InnerText;
                return text;
            }
        }

        // Takes raw text and makes it lower case. It keeps only letters and
        // apostrophes and turns everything else into a space. After that it
        // removes the extra spaces. We keep letters with accents (like n with
        // a tilde or r with a hacek) because they help tell languages apart.
        public string Clean(string raw)
        {
            bool isEmpty = IsNullOrEmpty(raw);
            if (isEmpty)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            int length = raw.Length;
            for (int i = 0; i < length; i++)
            {
                char ch = raw[i];
                bool isLetter = IsLetter(ch);
                bool isApostrophe = IsApostrophe(ch);
                if (isLetter)
                {
                    char lowerChar = MakeLowerCase(ch);
                    builder.Append(lowerChar);
                }
                else if (isApostrophe)
                {
                    builder.Append('\'');
                }
                else
                {
                    builder.Append(' ');
                }
            }
            string rawJoined = builder.ToString();
            List<string> wordList = SplitOnSpaces(rawJoined);
            string result = JoinWithSpaces(wordList);
            return result;
        }

        // Splits cleaned text into a list of words, using spaces as separators.
        public List<string> Tokenize(string cleaned)
        {
            bool isEmpty = IsNullOrEmpty(cleaned);
            if (isEmpty)
            {
                List<string> empty = new List<string>();
                return empty;
            }
            List<string> words = SplitOnSpaces(cleaned);
            return words;
        }
    }

    // Computes basic statistics about a cleaned, tokenized text sample.
    // These are written to the batch report to give context for each analysis.
    public class TextStats
    {
        public int WordCount { get; private set; }
        public int UniqueWordCount { get; private set; }
        public double AverageWordLength { get; private set; }
        public int LongWordCount { get; private set; }
        public string LongestWord { get; private set; }

        private TextStats()
        {
            LongestWord = "";
        }

        // Builds a TextStats object by counting and measuring the token list.
        public static TextStats Compute(List<string> tokens)
        {
            TextStats stats = new TextStats();
            stats.WordCount = CountWords(tokens);
            stats.UniqueWordCount = CountUniqueWords(tokens);
            stats.AverageWordLength = ComputeAverageLength(tokens);
            stats.LongWordCount = CountLongWords(tokens, 7);
            stats.LongestWord = FindLongestWord(tokens);
            return stats;
        }

        private static int CountWords(List<string> tokens)
        {
            int count = tokens.Count;
            return count;
        }

        private static int CountUniqueWords(List<string> tokens)
        {
            HashSet<string> unique = new HashSet<string>();
            int total = tokens.Count;
            for (int i = 0; i < total; i++)
            {
                string word = tokens[i];
                unique.Add(word);
            }
            int uniqueCount = unique.Count;
            return uniqueCount;
        }

        private static double ComputeAverageLength(List<string> tokens)
        {
            int total = tokens.Count;
            if (total == 0)
            {
                return 0.0;
            }
            int lengthSum = 0;
            for (int i = 0; i < total; i++)
            {
                string word = tokens[i];
                int wordLen = word.Length;
                lengthSum = lengthSum + wordLen;
            }
            double sumDouble = (double)lengthSum;
            double totalDouble = (double)total;
            double average = sumDouble / totalDouble;
            return average;
        }

        // Counts how many words are at least minLength characters long.
        private static int CountLongWords(List<string> tokens, int minLength)
        {
            int count = 0;
            int total = tokens.Count;
            for (int i = 0; i < total; i++)
            {
                string word = tokens[i];
                bool isLong = word.Length >= minLength;
                if (isLong)
                {
                    count = count + 1;
                }
            }
            return count;
        }

        private static string FindLongestWord(List<string> tokens)
        {
            int total = tokens.Count;
            if (total == 0)
            {
                return "";
            }
            string longest = tokens[0];
            for (int i = 1; i < total; i++)
            {
                string word = tokens[i];
                bool longerThanCurrent = word.Length > longest.Length;
                if (longerThanCurrent)
                {
                    longest = word;
                }
            }
            return longest;
        }

        // Returns the ratio of unique words to total words, between 0.0 and 1.0.
        public double TypeTokenRatio()
        {
            int total = WordCount;
            if (total == 0)
            {
                return 0.0;
            }
            double uniqueDouble = (double)UniqueWordCount;
            double totalDouble = (double)total;
            double ratio = uniqueDouble / totalDouble;
            return ratio;
        }

        // Returns true when there are at least minWords words in the text.
        // Shorter texts are harder to classify reliably.
        public bool HasEnoughWords(int minWords)
        {
            bool enough = WordCount >= minWords;
            return enough;
        }

        // Returns true when the type-token ratio is at or above the threshold.
        public bool IsRichVocabulary(double threshold)
        {
            double ttr = TypeTokenRatio();
            bool rich = ttr >= threshold;
            return rich;
        }

        // Writes a two-line statistics block to the report writer.
        public void WriteToReport(StreamWriter writer)
        {
            string wordCountStr = WordCount.ToString();
            string uniqueStr = UniqueWordCount.ToString();
            string avgStr = AverageWordLength.ToString("F1");
            string ttrStr = TypeTokenRatio().ToString("F2");
            string longStr = LongWordCount.ToString();
            string longestStr = LongestWord.Length > 0 ? LongestWord : "n/a";
            writer.WriteLine("  Stats : " + wordCountStr + " words, "
                + uniqueStr + " unique, avg-len=" + avgStr);
            writer.WriteLine("         ttr=" + ttrStr
                + " long-words=" + longStr
                + " longest=" + longestStr);
        }
    }
}
