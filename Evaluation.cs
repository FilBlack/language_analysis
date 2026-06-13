using System;
using System.Collections.Generic;
using System.IO;

namespace LanguageDetector
{
    // One labelled sample from the test file.
    public class EvaluationSample
    {
        public string TrueLanguage { get; set; }
        public string Text { get; set; }

        public EvaluationSample(string trueLanguage, string text)
        {
            TrueLanguage = trueLanguage;
            Text = text;
        }
    }

    // Accumulates prediction outcomes for one analyzer and prints a summary.
    public class AnalyzerStats
    {
        public string AnalyzerName { get; private set; }

        // Per-language correct and total counts.
        private Dictionary<string, int> correctPerLanguage;
        private Dictionary<string, int> totalPerLanguage;
        private int totalCorrect;
        private int totalSamples;

        public AnalyzerStats(string analyzerName)
        {
            AnalyzerName = analyzerName;
            correctPerLanguage = new Dictionary<string, int>();
            totalPerLanguage = new Dictionary<string, int>();
            totalCorrect = 0;
            totalSamples = 0;
        }

        // Records one prediction. trueLanguage is the ground truth.
        public void Record(string trueLanguage, string predictedLanguage)
        {
            bool alreadyTracked = totalPerLanguage.ContainsKey(trueLanguage);
            if (!alreadyTracked)
            {
                totalPerLanguage[trueLanguage] = 0;
                correctPerLanguage[trueLanguage] = 0;
            }
            totalPerLanguage[trueLanguage] = totalPerLanguage[trueLanguage] + 1;
            totalSamples = totalSamples + 1;
            bool correct = predictedLanguage == trueLanguage;
            if (correct)
            {
                correctPerLanguage[trueLanguage] = correctPerLanguage[trueLanguage] + 1;
                totalCorrect = totalCorrect + 1;
            }
        }

        // Returns the overall accuracy as a value between 0.0 and 1.0.
        public double OverallAccuracy()
        {
            if (totalSamples == 0)
            {
                return 0.0;
            }
            double correct = (double)totalCorrect;
            double total = (double)totalSamples;
            double accuracy = correct / total;
            return accuracy;
        }

        // Returns the accuracy for one specific language.
        public double AccuracyForLanguage(string language)
        {
            bool hasLanguage = totalPerLanguage.TryGetValue(language, out int total);
            if (!hasLanguage || total == 0)
            {
                return 0.0;
            }
            bool hasCorrect = correctPerLanguage.TryGetValue(language, out int correct);
            if (!hasCorrect)
            {
                return 0.0;
            }
            double accuracy = (double)correct / (double)total;
            return accuracy;
        }

        // Returns how many samples were seen for one language.
        public int SamplesForLanguage(string language)
        {
            bool found = totalPerLanguage.TryGetValue(language, out int total);
            if (!found)
            {
                return 0;
            }
            return total;
        }

        // Returns how many of the predictions for one language were correct.
        public int CorrectForLanguage(string language)
        {
            bool found = correctPerLanguage.TryGetValue(language, out int correct);
            if (!found)
            {
                return 0;
            }
            return correct;
        }

        public int TotalSamples { get { return totalSamples; } }
        public int TotalCorrect { get { return totalCorrect; } }
    }

    // Accumulates majority-vote consensus stats separately from individual analyzers.
    public class ConsensusStats
    {
        private int totalCorrect;
        private int totalSamples;

        public ConsensusStats()
        {
            totalCorrect = 0;
            totalSamples = 0;
        }

        public void Record(string trueLanguage, string consensusLanguage)
        {
            totalSamples = totalSamples + 1;
            bool correct = consensusLanguage == trueLanguage;
            if (correct)
            {
                totalCorrect = totalCorrect + 1;
            }
        }

        public double OverallAccuracy()
        {
            if (totalSamples == 0)
            {
                return 0.0;
            }
            return (double)totalCorrect / (double)totalSamples;
        }

        public int TotalCorrect { get { return totalCorrect; } }
        public int TotalSamples { get { return totalSamples; } }
    }

    // Reads a TSV test file, runs all analyzers, and prints accuracy results.
    public static class Evaluator
    {
        // Reads the test file. Each line must be: langcode TAB text
        public static List<EvaluationSample> LoadSamples(string path)
        {
            List<EvaluationSample> samples = new List<EvaluationSample>();
            string[] lines = File.ReadAllLines(path);
            int lineCount = lines.Length;
            for (int i = 0; i < lineCount; i++)
            {
                string line = lines[i];
                bool blank = line == null || line.Trim().Length == 0;
                if (blank)
                {
                    continue;
                }
                int tabPos = line.IndexOf('\t');
                bool hasTab = tabPos >= 0;
                if (!hasTab)
                {
                    continue;
                }
                string langCode = line.Substring(0, tabPos).Trim();
                string text = line.Substring(tabPos + 1).Trim();
                bool hasContent = langCode.Length > 0 && text.Length > 0;
                if (hasContent)
                {
                    EvaluationSample sample = new EvaluationSample(langCode, text);
                    samples.Add(sample);
                }
            }
            return samples;
        }

        // Finds the most common string in a list of predictions.
        private static string MajorityVote(List<string> predictions)
        {
            string best = "unknown";
            int bestCount = 0;
            int total = predictions.Count;
            for (int i = 0; i < total; i++)
            {
                string candidate = predictions[i];
                int count = 0;
                for (int j = 0; j < total; j++)
                {
                    if (predictions[j] == candidate)
                    {
                        count = count + 1;
                    }
                }
                bool newBest = count > bestCount;
                if (newBest)
                {
                    bestCount = count;
                    best = candidate;
                }
            }
            return best;
        }

        // Formats an accuracy value as a percentage string, e.g. "83.3%".
        private static string FormatAccuracy(double accuracy)
        {
            double percent = accuracy * 100.0;
            string formatted = percent.ToString("F1") + "%";
            return formatted;
        }

        // Prints a separator line of dashes.
        private static void PrintSeparator()
        {
            string sep = new string('-', 60);
            Console.WriteLine(sep);
        }

        // Runs the full evaluation and prints the results to the console.
        public static void Run(List<EvaluationSample> samples,
            List<LanguageAnalyzer> analyzers, TextCleaner cleaner)
        {
            int sampleCount = samples.Count;
            int analyzerCount = analyzers.Count;

            Console.WriteLine("Running evaluation on " + sampleCount + " samples...");
            PrintSeparator();

            // Set up one AnalyzerStats per analyzer plus one for consensus.
            List<AnalyzerStats> statsList = new List<AnalyzerStats>();
            for (int a = 0; a < analyzerCount; a++)
            {
                string name = analyzers[a].Name;
                AnalyzerStats stats = new AnalyzerStats(name);
                statsList.Add(stats);
            }
            ConsensusStats consensusStats = new ConsensusStats();

            // Run every sample through every analyzer.
            for (int i = 0; i < sampleCount; i++)
            {
                EvaluationSample sample = samples[i];
                string trueLanguage = sample.TrueLanguage;
                string cleaned = cleaner.Clean(sample.Text);

                List<string> predictions = new List<string>();

                for (int a = 0; a < analyzerCount; a++)
                {
                    LanguageAnalyzer analyzer = analyzers[a];
                    AnalysisResult result = analyzer.Analyze(cleaned);
                    string predicted = result.PredictedLanguage;
                    statsList[a].Record(trueLanguage, predicted);
                    predictions.Add(predicted);
                }

                string consensus = MajorityVote(predictions);
                consensusStats.Record(trueLanguage, consensus);
            }

            // Print overall accuracy table.
            Console.WriteLine();
            Console.WriteLine("Overall accuracy");
            PrintSeparator();

            int nameWidth = 26;
            for (int a = 0; a < analyzerCount; a++)
            {
                AnalyzerStats stats = statsList[a];
                string name = stats.AnalyzerName;
                string acc = FormatAccuracy(stats.OverallAccuracy());
                string correct = stats.TotalCorrect.ToString();
                string total = stats.TotalSamples.ToString();
                string paddedName = name.PadRight(nameWidth);
                Console.WriteLine(paddedName + acc + "  (" + correct + "/" + total + ")");
            }

            string consAcc = FormatAccuracy(consensusStats.OverallAccuracy());
            string consCorrect = consensusStats.TotalCorrect.ToString();
            string consTotal = consensusStats.TotalSamples.ToString();
            Console.WriteLine("Consensus (majority vote)  " + consAcc
                + "  (" + consCorrect + "/" + consTotal + ")");

            // Print per-language breakdown.
            List<string> languages = Languages.Supported;
            int langCount = languages.Count;

            for (int a = 0; a < analyzerCount; a++)
            {
                AnalyzerStats stats = statsList[a];
                Console.WriteLine();
                Console.WriteLine("Per-language: " + stats.AnalyzerName);
                PrintSeparator();

                for (int l = 0; l < langCount; l++)
                {
                    string lang = languages[l];
                    string displayName = Languages.DisplayName(lang);
                    int correct = stats.CorrectForLanguage(lang);
                    int total = stats.SamplesForLanguage(lang);
                    double acc = stats.AccuracyForLanguage(lang);
                    string accStr = FormatAccuracy(acc);
                    string paddedName = displayName.PadRight(10);
                    Console.WriteLine("  " + paddedName + correct + "/" + total
                        + "  " + accStr);
                }
            }

            Console.WriteLine();
            PrintSeparator();
            Console.WriteLine("Evaluation complete.");
        }
    }
}
