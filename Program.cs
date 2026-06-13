using System;
using System.Collections.Generic;
using System.IO;

namespace LanguageDetector
{
    public class Program
    {
        public static int Main(string[] args)
        {
            string inputFile = null;
            string outputFile = null;
            string evalFile = null;
            string resourcesDir = ".";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--out")
                {
                    i = i + 1;
                    outputFile = args[i];
                }
                else if (args[i] == "--resources")
                {
                    i = i + 1;
                    resourcesDir = args[i];
                }
                else if (args[i] == "--eval")
                {
                    i = i + 1;
                    evalFile = args[i];
                }
                else
                {
                    inputFile = args[i];
                }
            }

            if (inputFile != null && outputFile == null)
            {
                outputFile = inputFile + ".report.txt";
            }

            TextCleaner cleaner = new TextCleaner();
            ValidateResourcesRoot(resourcesDir);

            if (evalFile != null)
            {
                List<LanguageAnalyzer> evalAnalyzers = CreateEvalAnalyzerList(cleaner);
                Console.WriteLine("Training analyzers...");
                TrainAllAnalyzers(evalAnalyzers, resourcesDir, Languages.Supported);
                List<EvaluationSample> samples = Evaluator.LoadSamples(evalFile);
                Evaluator.Run(samples, evalAnalyzers, cleaner);
                return 0;
            }
            else if (inputFile == null)
            {
                List<LanguageAnalyzer> interactiveAnalyzers = CreateInteractiveAnalyzerList(cleaner);
                Console.WriteLine("Training analyzers...");
                TrainAllAnalyzers(interactiveAnalyzers, resourcesDir, Languages.Supported);
                return RunInteractive(cleaner, interactiveAnalyzers);
            }
            else
            {
                List<LanguageAnalyzer> analyzers = CreateInteractiveAnalyzerList(cleaner);
                Console.WriteLine("Training analyzers...");
                TrainAllAnalyzers(analyzers, resourcesDir, Languages.Supported);
                return RunBatch(cleaner, analyzers, inputFile, outputFile);
            }
        }


        // Creates the analyzer list for interactive mode: all three n-gram sizes
        // plus stop-word and Naive Bayes, so the user can compare them side by side.
        private static List<LanguageAnalyzer> CreateInteractiveAnalyzerList(TextCleaner cleaner)
        {
            List<LanguageAnalyzer> analyzers = new List<LanguageAnalyzer>();
            analyzers.Add(new NGramAnalyzer(cleaner, 2));
            analyzers.Add(new NGramAnalyzer(cleaner, 3));
            analyzers.Add(new NGramAnalyzer(cleaner, 4));
            analyzers.Add(new StopWordAnalyzer(cleaner));
            analyzers.Add(new NaiveBayesAnalyzer(cleaner));
            return analyzers;
        }

        // Creates the analyzer list used during evaluation: three separate NGram
        // analyzers (n=2, n=3, n=4) plus stop-word and Naive Bayes, so the eval
        // output shows how n-gram size affects accuracy.
        private static List<LanguageAnalyzer> CreateEvalAnalyzerList(TextCleaner cleaner)
        {
            List<LanguageAnalyzer> analyzers = new List<LanguageAnalyzer>();
            analyzers.Add(new NGramAnalyzer(cleaner, 2));
            analyzers.Add(new NGramAnalyzer(cleaner, 3));
            analyzers.Add(new NGramAnalyzer(cleaner, 4));
            analyzers.Add(new StopWordAnalyzer(cleaner));
            analyzers.Add(new NaiveBayesAnalyzer(cleaner));
            return analyzers;
        }

        // Calls Train on every analyzer in the list.
        private static void TrainAllAnalyzers(List<LanguageAnalyzer> analyzers,
            string resourcesRoot, List<string> languages)
        {
            int count = analyzers.Count;
            for (int i = 0; i < count; i++)
            {
                LanguageAnalyzer analyzer = analyzers[i];
                analyzer.Train(resourcesRoot, languages);
            }
        }

        // Warns the user if the resources folder is missing the expected sub-folders.
        private static void ValidateResourcesRoot(string root)
        {
            string trainingDir = Path.Combine(root, "training");
            string stopwordsDir = Path.Combine(root, "stopwords");
            bool trainingExists = Directory.Exists(trainingDir);
            bool stopwordsExists = Directory.Exists(stopwordsDir);
            if (!trainingExists)
            {
                Console.WriteLine("Warning: training folder not found at " + trainingDir);
            }
            if (!stopwordsExists)
            {
                Console.WriteLine("Warning: stopwords folder not found at " + stopwordsDir);
            }
        }

        // Builds a comma-separated string listing all the analyzer names.
        private static string FormatAnalyzerNames(List<LanguageAnalyzer> analyzers)
        {
            string nameList = "";
            int count = analyzers.Count;
            for (int i = 0; i < count; i++)
            {
                bool notFirst = i > 0;
                if (notFirst)
                {
                    nameList = nameList + ", ";
                }
                LanguageAnalyzer analyzer = analyzers[i];
                string analyzerName = analyzer.Name;
                nameList = nameList + analyzerName;
            }
            return nameList;
        }

        // Returns a short preview of a raw input line, cut to maxLength characters.
        private static string FormatPreview(string raw, int maxLength)
        {
            int rawLength = raw.Length;
            bool fitsInFull = rawLength <= maxLength;
            if (fitsInFull)
            {
                return raw;
            }
            int cutAt = maxLength - 3;
            string shortened = raw.Substring(0, cutAt);
            string preview = shortened + "...";
            return preview;
        }

        // Returns a string of repeated characters used as a separator line.
        private static string MakeSeparatorLine(char ch, int length)
        {
            string line = new string(ch, length);
            return line;
        }

        // Returns a timestamp string in the format "yyyy-MM-dd HH:mm:ss".
        private static string GetTimestamp()
        {
            DateTime now = DateTime.Now;
            string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss");
            return timestamp;
        }

        // Returns true if a line is null, empty, or contains only whitespace.
        private static bool IsBlankLine(string line)
        {
            if (line == null)
            {
                return true;
            }
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }
            return false;
        }

        // Prints a startup message showing the input path and the number of lines
        // that will actually be analyzed (blank lines excluded).
        private static void PrintBatchStartMessage(string inputPath, int lineCount)
        {
            string lineCountStr = lineCount.ToString();
            Console.WriteLine("Input     : " + inputPath);
            Console.WriteLine("Non-blank : " + lineCountStr + " lines to analyze");
        }

        // Returns true when the number of processed items is a multiple of the
        // interval, so we can print a progress message every N items.
        private static bool ShouldPrintProgress(int done, int interval)
        {
            bool isMultiple = done % interval == 0;
            return isMultiple;
        }

        // Writes a short progress update to the console.
        private static void PrintProgressUpdate(int done, int total)
        {
            string doneStr = done.ToString();
            string totalStr = total.ToString();
            Console.WriteLine("  processed " + doneStr + " of " + totalStr + " lines...");
        }

        // Counts how many non-blank lines are in the input array.
        private static int CountNonBlankLines(string[] lines)
        {
            int count = 0;
            int total = lines.Length;
            for (int i = 0; i < total; i++)
            {
                string line = lines[i];
                bool blank = IsBlankLine(line);
                if (!blank)
                {
                    count = count + 1;
                }
            }
            return count;
        }

        // Writes the header block at the top of a batch report file.
        private static void WriteReportHeader(StreamWriter writer, string inputPath,
            List<LanguageAnalyzer> analyzers)
        {
            string timestamp = GetTimestamp();
            string analyzerNames = FormatAnalyzerNames(analyzers);
            string separator = MakeSeparatorLine('=', 70);
            writer.WriteLine("Language Detection Report");
            writer.WriteLine("Generated : " + timestamp);
            writer.WriteLine("Input file: " + inputPath);
            writer.WriteLine("Analyzers : " + analyzerNames);
            writer.WriteLine(separator);
            writer.WriteLine();
        }

        // Writes the summary block at the end of a batch report file.
        private static void WriteReportSummary(StreamWriter writer, int analyzed,
            int fullAgreement, int partialAgreement, int noAgreement,
            List<LanguageAnalyzer> analyzers, int[] analyzerAgreements,
            Dictionary<string, int> languageLineCounts,
            Dictionary<string, int[]> languageAnalyzerAgreements)
        {
            string separator = MakeSeparatorLine('=', 70);
            string dashSeparator = MakeSeparatorLine('-', 70);
            int totalVoters = analyzers.Count;
            string totalStr = totalVoters.ToString();
            int analyzerCount = analyzers.Count;
            writer.WriteLine("Summary");
            writer.WriteLine(separator);
            writer.WriteLine("Lines analyzed                     : " + analyzed);
            writer.WriteLine("Full agreement (" + totalStr + "/" + totalStr + ")          : " + fullAgreement);
            writer.WriteLine("Partial agreement                  : " + partialAgreement);
            writer.WriteLine("No agreement                       : " + noAgreement);
            writer.WriteLine();
            writer.WriteLine("Overall agreement with consensus");
            writer.WriteLine(dashSeparator);
            for (int a = 0; a < analyzerCount; a++)
            {
                string name = analyzers[a].Name;
                int agreed = analyzerAgreements[a];
                double pct = analyzed == 0 ? 0.0 : (double)agreed / (double)analyzed * 100.0;
                string pctStr = pct.ToString("F1") + "%";
                string paddedName = name.PadRight(28);
                writer.WriteLine(paddedName + agreed + "/" + analyzed + "  " + pctStr);
            }
            List<string> languages = Languages.Supported;
            int langCount = languages.Count;
            for (int l = 0; l < langCount; l++)
            {
                string lang = languages[l];
                bool hasSamples = languageLineCounts.ContainsKey(lang);
                if (!hasSamples)
                {
                    continue;
                }
                int langTotal = languageLineCounts[lang];
                int[] langAgreements = languageAnalyzerAgreements[lang];
                string displayName = Languages.DisplayName(lang);
                writer.WriteLine();
                writer.WriteLine(displayName + " (" + langTotal + " lines)");
                writer.WriteLine(dashSeparator);
                for (int a = 0; a < analyzerCount; a++)
                {
                    string name = analyzers[a].Name;
                    int agreed = langAgreements[a];
                    double pct = langTotal == 0 ? 0.0 : (double)agreed / (double)langTotal * 100.0;
                    string pctStr = pct.ToString("F1") + "%";
                    string paddedName = name.PadRight(28);
                    writer.WriteLine(paddedName + agreed + "/" + langTotal + "  " + pctStr);
                }
            }
        }

        // Formats a confidence value as a percentage string
        private static string FormatConfidence(double confidence)
        {
            string confStr = confidence.ToString("F0");
            string formatted = confStr + "%";
            return formatted;
        }


        // Writes one analyzer's prediction line to the report, including confidence.
        private static void WriteAnalyzerResult(StreamWriter writer, string analyzerName,
            string displayName, string topThree, double confidence)
        {
            string confStr = FormatConfidence(confidence);
            string resultLine = "  " + analyzerName + ": " + displayName
                + " [" + confStr + " conf] (top: " + topThree + ")";
            writer.WriteLine(resultLine);
        }

        // Formats the top 3 predicted languages from one analysis result.
        private static string FormatTopThree(AnalysisResult result)
        {
            string top = "";
            int shown = 0;
            int limit = 3;
            int scoreCount = result.Scores.Count;
            for (int i = 0; i < scoreCount; i++)
            {
                bool reachedLimit = shown >= limit;
                if (reachedLimit)
                {
                    break;
                }
                bool notFirst = shown > 0;
                if (notFirst)
                {
                    top = top + ", ";
                }
                LanguageScore score = result.Scores[i];
                string langCode = score.Language;
                top = top + langCode;
                shown = shown + 1;
            }
            return top;
        }

        // Formats the consensus line that appears after all analyzer predictions.
        private static string FormatConsensusLine(string displayName, int votes, int total)
        {
            string votesStr = votes.ToString();
            string totalStr = total.ToString();
            string votesSummary = votesStr + "/" + totalStr + " agree";
            string line = "  Consensus: " + displayName + " (" + votesSummary + ")";
            return line;
        }

        // Formats the label shown at the start of each line entry in the report.
        private static string GetLineLabel(int lineNumber)
        {
            string numberStr = lineNumber.ToString();
            string label = "[Line " + numberStr + "] ";
            return label;
        }

        // Returns true when every analyzer voted for the same language.
        private static bool IsFullAgreement(int topVotes, int totalVoters)
        {
            bool fullAgree = topVotes == totalVoters;
            return fullAgree;
        }

        // Returns true when more than one but not all analyzers agreed.
        private static bool IsPartialAgreement(int topVotes, int totalVoters)
        {
            bool moreThanOne = topVotes > 1;
            bool notAll = topVotes < totalVoters;
            bool partial = moreThanOne && notAll;
            return partial;
        }

        // Runs all analyzers on one cleaned input string and returns the results.
        private static List<AnalysisResult> AnalyzeText(List<LanguageAnalyzer> analyzers,
            string cleaned)
        {
            List<AnalysisResult> results = new List<AnalysisResult>();
            int count = analyzers.Count;
            for (int i = 0; i < count; i++)
            {
                LanguageAnalyzer analyzer = analyzers[i];
                AnalysisResult result = analyzer.Analyze(cleaned);
                results.Add(result);
            }
            return results;
        }

        // Collects the predicted language from each result into a plain list.
        private static List<string> CollectPredictions(List<AnalysisResult> results)
        {
            List<string> predictions = new List<string>();
            int count = results.Count;
            for (int i = 0; i < count; i++)
            {
                AnalysisResult result = results[i];
                string predicted = result.PredictedLanguage;
                predictions.Add(predicted);
            }
            return predictions;
        }

        // Interactive mode: the user types a string and we show what each
        // analyzer thinks the language is. An empty line stops the program.
        private static int RunInteractive(TextCleaner cleaner, List<LanguageAnalyzer> analyzers)
        {
            Console.WriteLine("Paste a string to analyze (empty line to quit):");
            while (true)
            {
                Console.Write("> ");
                string line = Console.ReadLine();
                bool blank = IsBlankLine(line);
                if (blank)
                {
                    break;
                }
                string cleaned = cleaner.Clean(line);
                List<AnalysisResult> results = AnalyzeText(analyzers, cleaned);
                int resultCount = results.Count;
                for (int i = 0; i < resultCount; i++)
                {
                    AnalysisResult result = results[i];
                    string analyzerName = analyzers[i].Name;
                    string predicted = result.PredictedLanguage;
                    string displayName = Languages.DisplayName(predicted);
                    Console.WriteLine("  " + analyzerName + ": " + displayName);
                }
                Console.WriteLine();
            }
            return 0;
        }

        // Batch mode: read every line of the input file, analyze each one, and
        // write a report that compares all three analyzers.
        private static int RunBatch(TextCleaner cleaner, List<LanguageAnalyzer> analyzers,
            string inputPath, string outputPath)
        {
            bool inputExists = File.Exists(inputPath);
            if (!inputExists)
            {
                Console.Error.WriteLine("Input file not found: " + inputPath);
                return 1;
            }
            string[] lines = File.ReadAllLines(inputPath);
            int nonBlankCount = CountNonBlankLines(lines);
            PrintBatchStartMessage(inputPath, nonBlankCount);
            int totalLines = lines.Length;
            int analyzed = 0;
            int fullAgreement = 0;
            int partialAgreement = 0;
            int noAgreement = 0;
            int totalVoters = analyzers.Count;
            int[] analyzerAgreements = new int[totalVoters];
            Dictionary<string, int> languageLineCounts = new Dictionary<string, int>();
            Dictionary<string, int[]> languageAnalyzerAgreements = new Dictionary<string, int[]>();
            string lineSeparator = MakeSeparatorLine('-', 70);
            using (StreamWriter writer = new StreamWriter(outputPath, false))
            {
                WriteReportHeader(writer, inputPath, analyzers);
                for (int i = 0; i < totalLines; i++)
                {
                    string raw = lines[i];
                    bool blank = IsBlankLine(raw);
                    if (blank)
                    {
                        continue;
                    }
                    analyzed = analyzed + 1;
                    bool printProgress = ShouldPrintProgress(analyzed, 50);
                    if (printProgress)
                    {
                        PrintProgressUpdate(analyzed, nonBlankCount);
                    }
                    string cleaned = cleaner.Clean(raw);
                    string preview = FormatPreview(raw, 64);
                    int lineNumber = i + 1;
                    string lineLabel = GetLineLabel(lineNumber);
                    writer.WriteLine(lineLabel + preview);
                    List<AnalysisResult> results = AnalyzeText(analyzers, cleaned);
                    List<string> predictions = CollectPredictions(results);
                    int resultCount = results.Count;
                    for (int a = 0; a < resultCount; a++)
                    {
                        AnalysisResult result = results[a];
                        string analyzerName = analyzers[a].Name;
                        string predicted = result.PredictedLanguage;
                        string displayName = Languages.DisplayName(predicted);
                        string topThree = FormatTopThree(result);
                        double confidence = result.Confidence;
                        WriteAnalyzerResult(writer, analyzerName, displayName, topThree, confidence);
                    }
                    string bestLanguage = FindMostCommon(predictions);
                    int topVotes = CountOccurrences(predictions, bestLanguage);
                    string bestDisplayName = Languages.DisplayName(bestLanguage);
                    string consensusLine = FormatConsensusLine(bestDisplayName, topVotes,
                        totalVoters);
                    writer.WriteLine(consensusLine);
                    writer.WriteLine(lineSeparator);
                    bool langSeen = languageLineCounts.ContainsKey(bestLanguage);
                    if (!langSeen)
                    {
                        languageLineCounts[bestLanguage] = 0;
                        languageAnalyzerAgreements[bestLanguage] = new int[totalVoters];
                    }
                    languageLineCounts[bestLanguage] = languageLineCounts[bestLanguage] + 1;
                    for (int a = 0; a < resultCount; a++)
                    {
                        bool agreedWithConsensus = predictions[a] == bestLanguage;
                        if (agreedWithConsensus)
                        {
                            analyzerAgreements[a] = analyzerAgreements[a] + 1;
                            languageAnalyzerAgreements[bestLanguage][a] =
                                languageAnalyzerAgreements[bestLanguage][a] + 1;
                        }
                    }
                    bool fullAgree = IsFullAgreement(topVotes, totalVoters);
                    bool partialAgree = IsPartialAgreement(topVotes, totalVoters);
                    if (fullAgree)
                    {
                        fullAgreement = fullAgreement + 1;
                    }
                    else if (partialAgree)
                    {
                        partialAgreement = partialAgreement + 1;
                    }
                    else
                    {
                        noAgreement = noAgreement + 1;
                    }
                }
                writer.WriteLine();
                WriteReportSummary(writer, analyzed, fullAgreement, partialAgreement,
                    noAgreement, analyzers, analyzerAgreements,
                    languageLineCounts, languageAnalyzerAgreements);
            }
            Console.WriteLine("Report written to: " + outputPath);
            return 0;
        }

        // Counts how many times one specific value appears in a list of strings.
        private static int CountOccurrences(List<string> values, string target)
        {
            int count = 0;
            int total = values.Count;
            for (int i = 0; i < total; i++)
            {
                string value = values[i];
                bool matches = value == target;
                if (matches)
                {
                    count = count + 1;
                }
            }
            return count;
        }

        // Finds the value that appears most often in a list.
        // Returns "unknown" when the list is empty.
        private static string FindMostCommon(List<string> values)
        {
            int total = values.Count;
            if (total == 0)
            {
                return "unknown";
            }
            string best = "unknown";
            int bestCount = 0;
            for (int i = 0; i < total; i++)
            {
                string candidate = values[i];
                int candidateCount = CountOccurrences(values, candidate);
                bool newBest = candidateCount > bestCount;
                if (newBest)
                {
                    bestCount = candidateCount;
                    best = candidate;
                }
            }
            return best;
        }
    }

}
