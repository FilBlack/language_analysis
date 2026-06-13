using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LanguageDetector
{
    // Holds one language together with the score an analyzer gave it.
    public class LanguageScore
    {
        public string Language { get; set; }
        public double Score { get; set; }

        public LanguageScore(string language, double score)
        {
            Language = language;
            Score = score;
        }
    }

    // The result of running one analyzer on one input string.
    public class AnalysisResult
    {
        public string AnalyzerName { get; set; }

        // All the languages with their scores, already sorted with the best language first.
        public List<LanguageScore> Scores { get; set; }

        // The winning language is just the first one in the sorted list.
        public string PredictedLanguage
        {
            get
            {
                bool hasScores = Scores != null && Scores.Count > 0;
                if (hasScores)
                {
                    LanguageScore best = Scores[0];
                    string lang = best.Language;
                    return lang;
                }
                return "unknown";
            }
        }

        // A percentage from 0 to 100 showing how clearly the winning language stood out from the runner-up, set by the analyzer after scoring.
        public double Confidence { get; set; }
    }

    // The list of languages the program can recognize and their full names.
    public static class Languages
    {
        public static readonly List<string> Supported = new List<string>()
        {
            "en", "cs", "sk", "de", "es", "it"
        };

        public static string DisplayName(string code)
        {
            if (code == "en") return "English";
            if (code == "cs") return "Czech";
            if (code == "sk") return "Slovak";
            if (code == "de") return "German";
            if (code == "es") return "Spanish";
            if (code == "it") return "Italian";
            return code;
        }
    }

    // This is the shared parent for all three detection methods. Each method
    // first builds its model in Train and then scores a string in Analyze.
    public abstract class LanguageAnalyzer
    {
        // The cleaner is used to read training files and to split text into words.
        protected TextCleaner Cleaner;

        public LanguageAnalyzer(TextCleaner cleaner)
        {
            Cleaner = cleaner;
        }

        public abstract string Name { get; }

        public abstract void Train(string resourcesRoot, List<string> languages);

        // Looks at one cleaned string and returns a score for each language,
        // sorted so that the most likely language comes first.
        public abstract AnalysisResult Analyze(string cleanedInput);

        // Creates a LanguageScore object for the given language and score value.
        protected LanguageScore MakeScore(string language, double score)
        {
            LanguageScore ls = new LanguageScore(language, score);
            return ls;
        }

        // Creates an AnalysisResult with the given sorted list of language scores.
        protected AnalysisResult MakeResult(List<LanguageScore> sortedScores)
        {
            AnalysisResult result = new AnalysisResult();
            result.AnalyzerName = Name;
            result.Scores = sortedScores;
            return result;
        }

        // Computes a confidence percentage for the top-ranked language.
        // For distance-based scores (lower is better, e.g. NGram) the gap between
        // first and second place is measured as a fraction of the second-place score.
        // For probability-based scores (higher is better) the gap is measured as a
        // fraction of the combined magnitude of first and second place.
        protected double ComputeConfidenceValue(List<LanguageScore> sortedScores,
            bool lowerIsBetter)
        {
            int count = sortedScores.Count;
            if (count == 0)
            {
                return 0.0;
            }
            if (count == 1)
            {
                return 100.0;
            }
            double bestScore = sortedScores[0].Score;
            double secondScore = sortedScores[1].Score;
            if (lowerIsBetter)
            {
                double confidence = ConfidenceForLowerBetter(bestScore, secondScore);
                return confidence;
            }
            else
            {
                double confidence = ConfidenceForHigherBetter(bestScore, secondScore);
                return confidence;
            }
        }

        // Confidence when a smaller score is better (e.g. distance metric).
        // The further the winner is ahead, the higher the confidence.
        private double ConfidenceForLowerBetter(double best, double second)
        {
            if (best == 0.0)
            {
                return 100.0;
            }
            if (second == 0.0)
            {
                return 0.0;
            }
            double gap = second - best;
            if (gap < 0.0)
            {
                gap = 0.0;
            }
            double confidence = gap / second * 100.0;
            if (confidence > 100.0)
            {
                confidence = 100.0;
            }
            return confidence;
        }

        // Confidence when a larger score is better (ratio or log-probability).
        // Uses absolute values so the formula works for negative log-probabilities.
        private double ConfidenceForHigherBetter(double best, double second)
        {
            double absSecond = Math.Abs(second);
            if (absSecond == 0.0)
            {
                return 100.0;
            }
            double absGap = Math.Abs(best - second);
            double confidence = absGap / absSecond * 100.0;
            if (confidence > 100.0)
            {
                confidence = 100.0;
            }
            return confidence;
        }

        // Returns true if the training folder for a given language exists on disk.
        protected bool TrainingDirectoryExists(string dirPath)
        {
            bool exists = Directory.Exists(dirPath);
            return exists;
        }

        // Helper: reads and cleans every training file for one language from the
        // folder {resourcesRoot}/training/{language}. Files we cannot read are
        // skipped. It returns one cleaned string for each file.
        protected List<string> LoadTrainingTexts(string resourcesRoot, string language)
        {
            List<string> texts = new List<string>();
            string dir = Path.Combine(resourcesRoot, "training", language);
            bool dirExists = TrainingDirectoryExists(dir);
            if (!dirExists)
            {
                return texts;
            }
            string[] files = Directory.GetFiles(dir);
            int fileCount = files.Length;
            for (int i = 0; i < fileCount; i++)
            {
                string file = files[i];
                try
                {
                    string raw = Cleaner.LoadFromFile(file);
                    string cleaned = Cleaner.Clean(raw);
                    texts.Add(cleaned);
                }
                catch (NotSupportedException)
                {
                    // This file is in a format we cannot read, so we skip it.
                }
            }
            return texts;
        }
    }

    // 1. N-gram analysis with the Out-of-Place metric.
    //
    // The idea: we rank the most common character n-grams of the input and
    // compare that ranking with each language. The penalty for an n-gram is how
    // far its rank moved between the two rankings (or a fixed maximum if it is
    // missing). The language with the smallest total penalty wins.
    public class NGramAnalyzer : LanguageAnalyzer
    {
        // The length of each n-gram (for example 3 letters).
        private readonly int N;

        // How many of the top n-grams we keep for each language.
        private const int profileSize = 300;

        // The penalty we give when an n-gram is not found in a language at all.
        private const int maxPenalty = 300;

        // For each language: a dictionary that maps an n-gram to its rank.
        // A rank of 1 means it is the most common n-gram.
        private readonly Dictionary<string, Dictionary<string, int>> profiles =
            new Dictionary<string, Dictionary<string, int>>();

        // Normal constructor – starts with an empty model; call Train() to build it.
        public NGramAnalyzer(TextCleaner cleaner, int n) : base(cleaner)
        {
            N = n;
        }

        // Loading constructor – restores a previously saved model from disk.
        public NGramAnalyzer(TextCleaner cleaner, int n, string modelPath) : base(cleaner)
        {
            N = n;
            string json = File.ReadAllText(modelPath);
            profiles = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json)
                ?? new Dictionary<string, Dictionary<string, int>>();
        }

        // Saves the trained model to a JSON file so it can be loaded later.
        public void Save(string path)
        {
            string json = JsonSerializer.Serialize(profiles);
            File.WriteAllText(path, json);
        }

        public override string Name
        {
            get { return "N-gram (n=" + N + ")"; }
        }

        // Returns true if a profile has already been trained for the given language.
        private bool ProfileExistsForLanguage(string language)
        {
            bool exists = profiles.ContainsKey(language);
            return exists;
        }

        // Returns the ranked profile for a language. If no profile has been
        // trained yet for that language, an empty dictionary is returned instead
        private Dictionary<string, int> GetLanguageProfile(string language)
        {
            bool hasProfile = ProfileExistsForLanguage(language);
            if (!hasProfile)
            {
                Dictionary<string, int> empty = new Dictionary<string, int>();
                return empty;
            }
            Dictionary<string, int> profile = profiles[language];
            return profile;
        }

        // Returns true when a word has at least N characters and can therefore
        // produce at least one n-gram of the required length.
        private bool WordIsLongEnough(string word)
        {
            bool longEnough = word.Length >= N;
            return longEnough;
        }

        // Extracts the n-gram that starts at position pos inside the word.
        private string GetNGramAtPosition(string word, int pos)
        {
            string ngram = word.Substring(pos, N);
            return ngram;
        }

        // Adds the count of one n-gram into a combined dictionary.
        // If the n-gram is already there, the new count is added on top.
        private void AddCountToDict(Dictionary<string, int> dict, string ngram, int count)
        {
            bool alreadyExists = dict.ContainsKey(ngram);
            if (alreadyExists)
            {
                int oldCount = dict[ngram];
                int newCount = oldCount + count;
                dict[ngram] = newCount;
            }
            else
            {
                dict[ngram] = count;
            }
        }

        // Merges all the n-gram counts from one text into a combined dictionary.
        private void MergeNGramCounts(Dictionary<string, int> combined,
            Dictionary<string, int> newCounts)
        {
            foreach (KeyValuePair<string, int> kv in newCounts)
            {
                string ngram = kv.Key;
                int count = kv.Value;
                AddCountToDict(combined, ngram, count);
            }
        }

        // Sorts the n-gram count dictionary from most common to least common.
        private List<KeyValuePair<string, int>> SortCountsDescending(
            Dictionary<string, int> counts)
        {
            List<KeyValuePair<string, int>> list =
                new List<KeyValuePair<string, int>>(counts);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        // Keeps only the first 'limit' entries from a sorted list.
        private List<KeyValuePair<string, int>> TakeTopEntries(
            List<KeyValuePair<string, int>> sorted, int limit)
        {
            List<KeyValuePair<string, int>> top = new List<KeyValuePair<string, int>>();
            int totalAvailable = sorted.Count;
            int takeCount = totalAvailable;
            if (limit < takeCount)
            {
                takeCount = limit;
            }
            for (int i = 0; i < takeCount; i++)
            {
                KeyValuePair<string, int> entry = sorted[i];
                top.Add(entry);
            }
            return top;
        }

        // Guards against a rank of zero or below.
        private int SanitizeRank(int rank)
        {
            bool positive = rank >= 1;
            if (positive)
            {
                return rank;
            }
            return 1;
        }

        // Turns the top entries into a dictionary that maps each n-gram to its rank.
        // The first entry gets rank 1, the second gets rank 2, and so on.
        private Dictionary<string, int> AssignRanks(List<KeyValuePair<string, int>> topEntries)
        {
            Dictionary<string, int> ranked = new Dictionary<string, int>();
            int entryCount = topEntries.Count;
            for (int i = 0; i < entryCount; i++)
            {
                string ngram = topEntries[i].Key;
                int rank = SanitizeRank(i + 1);
                ranked[ngram] = rank;
            }
            return ranked;
        }

        // Extracts all character n-grams from one word and adds them to the dictionary.
        private void ExtractNGramsFromWord(string word, Dictionary<string, int> dict)
        {
            int lastStart = word.Length - N;
            for (int i = 0; i <= lastStart; i++)
            {
                string ngram = GetNGramAtPosition(word, i);
                bool exists = dict.ContainsKey(ngram);
                if (exists)
                {
                    int oldCount = dict[ngram];
                    dict[ngram] = oldCount + 1;
                }
                else
                {
                    dict[ngram] = 1;
                }
            }
        }

        // Makes the character n-grams of the text by splitting it into words
        // and extracting every n-gram from each word.
        public Dictionary<string, int> GenerateNGrams(string cleanedText)
        {
            Dictionary<string, int> ngramDict = new Dictionary<string, int>();
            string[] words = cleanedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int wordCount = words.Length;
            for (int w = 0; w < wordCount; w++)
            {
                string word = words[w];
                bool longEnough = WordIsLongEnough(word);
                if (longEnough)
                {
                    ExtractNGramsFromWord(word, ngramDict);
                }
            }
            return ngramDict;
        }

        // Turns raw n-gram counts into a ranked profile. Rank 1 is the most
        // common n-gram. We only keep the top 'size' n-grams.
        private Dictionary<string, int> BuildRankedProfile(Dictionary<string, int> counts, int size)
        {
            List<KeyValuePair<string, int>> sorted = SortCountsDescending(counts);
            List<KeyValuePair<string, int>> top = TakeTopEntries(sorted, size);
            Dictionary<string, int> profile = AssignRanks(top);
            return profile;
        }

        // Computes the rank penalty for one n-gram when comparing an input profile
        // to a language profile.
        private int PenaltyForOneNGram(string ngram, int inputRank,
            Dictionary<string, int> languageProfile)
        {
            bool foundInLanguage = languageProfile.TryGetValue(ngram, out int langRank);
            if (foundInLanguage)
            {
                int difference = inputRank - langRank;
                if (difference < 0)
                {
                    difference = -difference;
                }
                return difference;
            }
            else
            {
                return maxPenalty;
            }
        }

        // Adds up the rank penalties for every n-gram in the input profile against
        // one language profile. A smaller total means a closer match.
        private int OutOfPlaceDistance(Dictionary<string, int> input,
            Dictionary<string, int> language)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> kv in input)
            {
                string ngram = kv.Key;
                int inputRank = kv.Value;
                int penalty = PenaltyForOneNGram(ngram, inputRank, language);
                total = total + penalty;
            }
            return total;
        }

        public override void Train(string resourcesRoot, List<string> languages)
        {
            int languageCount = languages.Count;
            for (int i = 0; i < languageCount; i++)
            {
                string language = languages[i];
                List<string> texts = LoadTrainingTexts(resourcesRoot, language);
                int textCount = texts.Count;
                Dictionary<string, int> combinedCounts = new Dictionary<string, int>();
                for (int t = 0; t < textCount; t++)
                {
                    string text = texts[t];
                    Dictionary<string, int> textCounts = GenerateNGrams(text);
                    MergeNGramCounts(combinedCounts, textCounts);
                }
                Dictionary<string, int> rankedProfile =
                    BuildRankedProfile(combinedCounts, profileSize);
                profiles[language] = rankedProfile;
            }
        }

        public override AnalysisResult Analyze(string cleanedInput)
        {
            Dictionary<string, int> rawInputCounts = GenerateNGrams(cleanedInput);
            Dictionary<string, int> inputProfile =
                BuildRankedProfile(rawInputCounts, profileSize);
            List<LanguageScore> scores = new List<LanguageScore>();
            foreach (KeyValuePair<string, Dictionary<string, int>> kv in profiles)
            {
                string language = kv.Key;
                Dictionary<string, int> languageProfile = GetLanguageProfile(language);
                int distance = OutOfPlaceDistance(inputProfile, languageProfile);
                LanguageScore score = MakeScore(language, distance);
                scores.Add(score);
            }
            scores.Sort((a, b) => a.Score.CompareTo(b.Score));
            AnalysisResult result = MakeResult(scores);
            result.Confidence = ComputeConfidenceValue(scores, true);
            return result;
        }
    }

    // 2. Detection based on stop-word frequency.
    public class StopWordAnalyzer : LanguageAnalyzer
    {
        // The language is the key and the set of stop-words is the value.
        private readonly Dictionary<string, HashSet<string>> stopWords =
            new Dictionary<string, HashSet<string>>();

        public StopWordAnalyzer(TextCleaner cleaner) : base(cleaner)
        {
        }

        public override string Name
        {
            get { return "Stop-word frequency"; }
        }

        // Builds the full file path to the stop-word list for one language.
        private string GetStopWordFilePath(string resourcesRoot, string language)
        {
            string fileName = language + ".txt";
            string filePath = Path.Combine(resourcesRoot, "stopwords", fileName);
            return filePath;
        }

        // Returns true if the stop-word file for the language exists on disk.
        private bool StopWordFileExists(string filePath)
        {
            bool exists = File.Exists(filePath);
            return exists;
        }

        // Reads all the words from a stop-word file and puts them into a HashSet.
        private HashSet<string> LoadWordSetFromFile(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            HashSet<string> wordSet = new HashSet<string>(lines);
            return wordSet;
        }

        // Returns true if the given word is found in the stop-word set.
        private bool IsStopWord(string word, HashSet<string> wordSet)
        {
            bool found = wordSet.Contains(word);
            return found;
        }

        // Returns true when stop-word data has been loaded for the given language.
        private bool LanguageHasStopWords(string language)
        {
            bool hasWords = stopWords.ContainsKey(language);
            return hasWords;
        }

        // Counts how many words in the token list appear in the stop-word set.
        private int CountStopWordHits(List<string> tokens, HashSet<string> wordSet)
        {
            int hits = 0;
            int tokenCount = tokens.Count;
            for (int i = 0; i < tokenCount; i++)
            {
                string token = tokens[i];
                bool isStop = IsStopWord(token, wordSet);
                if (isStop)
                {
                    hits = hits + 1;
                }
            }
            return hits;
        }

        // Works out what fraction of the words are stop-words.
        private double StopWordRatio(List<string> tokens, HashSet<string> wordSet)
        {
            int total = tokens.Count;
            if (total == 0)
            {
                return 0.0;
            }
            int hits = CountStopWordHits(tokens, wordSet);
            double hitsAsDouble = (double)hits;
            double totalAsDouble = (double)total;
            double ratio = hitsAsDouble / totalAsDouble;
            return ratio;
        }

        // Returns a list of all the languages that have been loaded into the model.
        private List<string> GetKnownLanguages()
        {
            List<string> languages = new List<string>();
            foreach (KeyValuePair<string, HashSet<string>> kv in stopWords)
            {
                string language = kv.Key;
                bool hasData = LanguageHasStopWords(language);
                if (hasData)
                {
                    languages.Add(language);
                }
            }
            return languages;
        }

        public override void Train(string resourcesRoot, List<string> languages)
        {
            int count = languages.Count;
            for (int i = 0; i < count; i++)
            {
                string language = languages[i];
                string filePath = GetStopWordFilePath(resourcesRoot, language);
                bool fileExists = StopWordFileExists(filePath);
                if (!fileExists)
                {
                    continue;
                }
                HashSet<string> wordSet = LoadWordSetFromFile(filePath);
                stopWords[language] = wordSet;
            }
        }

        public override AnalysisResult Analyze(string cleanedInput)
        {
            List<string> tokens = Cleaner.Tokenize(cleanedInput);
            List<LanguageScore> scores = new List<LanguageScore>();
            List<string> knownLanguages = GetKnownLanguages();
            int languageCount = knownLanguages.Count;
            for (int i = 0; i < languageCount; i++)
            {
                string language = knownLanguages[i];
                HashSet<string> wordSet = stopWords[language];
                double ratio = StopWordRatio(tokens, wordSet);
                LanguageScore score = MakeScore(language, ratio);
                scores.Add(score);
            }
            scores.Sort((a, b) => b.Score.CompareTo(a.Score));
            AnalysisResult result = MakeResult(scores);
            result.Confidence = ComputeConfidenceValue(scores, false);
            return result;
        }
    }

    // 3. A simple Naive Bayes classifier.
    //
    // For each language this works out how likely the input is by adding up
    // log P(language) and the log P(word | language) for every word. We use logs
    // (and add a small amount with smoothing) so that multiplying lots of tiny
    // probabilities together does not turn the answer into zero.
    public class NaiveBayesAnalyzer : LanguageAnalyzer
    {
        // The smoothing value (1.0 is the normal Laplace smoothing).
        private const double alpha = 1.0;

        // For each language: the log of the prior probability P(language).
        private readonly Dictionary<string, double> logPriors =
            new Dictionary<string, double>();

        // For each language: how many times each word was seen during training.
        private readonly Dictionary<string, Dictionary<string, int>> tokenCounts =
            new Dictionary<string, Dictionary<string, int>>();

        // For each language: the total number of training words.
        private readonly Dictionary<string, long> totalTokens =
            new Dictionary<string, long>();

        // The number of distinct words seen across all languages combined.
        private int vocabularySize;

        public NaiveBayesAnalyzer(TextCleaner cleaner) : base(cleaner)
        {
        }

        public override string Name
        {
            get { return "Naive Bayes"; }
        }

        // Returns true if a count dictionary has already been set up for the language.
        private bool IsLanguageInitialized(string language)
        {
            bool hasLanguage = tokenCounts.ContainsKey(language);
            return hasLanguage;
        }

        // Returns the total number of training words seen for one language.
        private long GetTotalWordCount(string language)
        {
            bool hasLanguage = totalTokens.TryGetValue(language, out long count);
            if (!hasLanguage)
            {
                return 0;
            }
            return count;
        }

        // Returns true when a token list has at least one word in it.
        // Empty texts are skipped so they do not affect the word counts.
        private bool TextHasWords(List<string> tokens)
        {
            bool hasWords = tokens.Count > 0;
            return hasWords;
        }

        // Sets up an empty word-count dictionary and a zero total for one language.
        private void InitLanguage(string language)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            tokenCounts[language] = counts;
            totalTokens[language] = 0;
        }

        // Adds one word to the count dictionary for the given language and
        // increments the total token count for that language.
        private void AddWordToLanguage(string language, string word)
        {
            Dictionary<string, int> counts = tokenCounts[language];
            bool wordAlreadySeen = counts.ContainsKey(word);
            if (wordAlreadySeen)
            {
                int oldCount = counts[word];
                int newCount = oldCount + 1;
                counts[word] = newCount;
            }
            else
            {
                counts[word] = 1;
            }
            long oldTotal = totalTokens[language];
            totalTokens[language] = oldTotal + 1;
        }

        // Tokenizes one training text and adds every word to the language model.
        // Texts that contain no words are skipped.
        private void ProcessOneText(string language, string cleanedText)
        {
            List<string> tokens = Cleaner.Tokenize(cleanedText);
            bool hasWords = TextHasWords(tokens);
            if (!hasWords)
            {
                return;
            }
            int tokenCount = tokens.Count;
            for (int i = 0; i < tokenCount; i++)
            {
                string word = tokens[i];
                AddWordToLanguage(language, word);
            }
        }

        // Adds all words seen in a language's count dictionary to the vocabulary set.
        private void AddLanguageWordsToVocabulary(string language, HashSet<string> vocabulary)
        {
            bool initialized = IsLanguageInitialized(language);
            if (!initialized)
            {
                return;
            }
            Dictionary<string, int> counts = tokenCounts[language];
            foreach (KeyValuePair<string, int> kv in counts)
            {
                string word = kv.Key;
                vocabulary.Add(word);
            }
        }

        // Gathers all unique words seen across all languages into one set.
        private HashSet<string> CollectGlobalVocabulary(List<string> languages)
        {
            HashSet<string> vocabulary = new HashSet<string>();
            int languageCount = languages.Count;
            for (int i = 0; i < languageCount; i++)
            {
                string language = languages[i];
                AddLanguageWordsToVocabulary(language, vocabulary);
            }
            return vocabulary;
        }

        // Sums up the total number of training words across all languages.
        private long SumAllTokens(List<string> languages)
        {
            long total = 0;
            int languageCount = languages.Count;
            for (int i = 0; i < languageCount; i++)
            {
                string language = languages[i];
                long langTotal = GetTotalWordCount(language);
                total = total + langTotal;
            }
            return total;
        }

        // Computes and stores the log prior for one language.
        private void ComputeLogPriorForLanguage(string language, long totalWordsAll)
        {
            if (totalWordsAll == 0)
            {
                logPriors[language] = 0.0;
                return;
            }
            long langWords = GetTotalWordCount(language);
            double langWordsDouble = (double)langWords;
            double totalDouble = (double)totalWordsAll;
            double share = langWordsDouble / totalDouble;
            double prior = Math.Log(share);
            logPriors[language] = prior;
        }

        // Computes and stores the log priors for all languages at once.
        private void ComputeAllLogPriors(List<string> languages, long totalWordsAll)
        {
            int count = languages.Count;
            for (int i = 0; i < count; i++)
            {
                string language = languages[i];
                ComputeLogPriorForLanguage(language, totalWordsAll);
            }
        }

        // Looks up how many times a word appeared for a language during training.
        private int GetWordCount(string language, string word)
        {
            bool hasLanguage = tokenCounts.TryGetValue(language,
                out Dictionary<string, int> counts);
            if (!hasLanguage)
            {
                return 0;
            }
            bool hasWord = counts.TryGetValue(word, out int count);
            if (!hasWord)
            {
                return 0;
            }
            return count;
        }

        // Returns the log of the prior probability for a language.
        private double LogPrior(string language)
        {
            bool found = logPriors.TryGetValue(language, out double prior);
            if (found)
            {
                return prior;
            }
            int numLanguages = logPriors.Count;
            if (numLanguages == 0)
            {
                numLanguages = 1;
            }
            double uniformShare = 1.0 / (double)numLanguages;
            double logUniform = Math.Log(uniformShare);
            return logUniform;
        }

        // Returns the log probability of a word in a language, with Laplace smoothing.
        private double LogLikelihood(string token, string language)
        {
            int rawCount = GetWordCount(language, token);
            double smoothedCount = (double)rawCount + alpha;
            long langTotal = GetTotalWordCount(language);
            double langTotalDouble = (double)langTotal;
            double smoothedDenominator = langTotalDouble + alpha * (double)vocabularySize;
            double probability = smoothedCount / smoothedDenominator;
            double logProbability = Math.Log(probability);
            return logProbability;
        }

        // Computes the total log score for one language given a list of tokens.
        private double ScoreLanguage(string language, List<string> tokens)
        {
            double score = LogPrior(language);
            int tokenCount = tokens.Count;
            for (int i = 0; i < tokenCount; i++)
            {
                string token = tokens[i];
                double wordScore = LogLikelihood(token, language);
                score = score + wordScore;
            }
            return score;
        }

        public override void Train(string resourcesRoot, List<string> languages)
        {
            int languageCount = languages.Count;
            for (int i = 0; i < languageCount; i++)
            {
                string language = languages[i];
                InitLanguage(language);
            }
            for (int i = 0; i < languageCount; i++)
            {
                string language = languages[i];
                List<string> texts = LoadTrainingTexts(resourcesRoot, language);
                int textCount = texts.Count;
                for (int t = 0; t < textCount; t++)
                {
                    string text = texts[t];
                    ProcessOneText(language, text);
                }
            }
            HashSet<string> vocabulary = CollectGlobalVocabulary(languages);
            vocabularySize = vocabulary.Count;
            long totalWordsAll = SumAllTokens(languages);
            ComputeAllLogPriors(languages, totalWordsAll);
        }

        public override AnalysisResult Analyze(string cleanedInput)
        {
            List<string> tokens = Cleaner.Tokenize(cleanedInput);
            List<string> knownLanguages = new List<string>(logPriors.Keys);
            int languageCount = knownLanguages.Count;
            List<LanguageScore> scores = new List<LanguageScore>();
            for (int i = 0; i < languageCount; i++)
            {
                string language = knownLanguages[i];
                double score = ScoreLanguage(language, tokens);
                LanguageScore ls = MakeScore(language, score);
                scores.Add(ls);
            }
            scores.Sort((a, b) => b.Score.CompareTo(a.Score));
            AnalysisResult result = MakeResult(scores);
            result.Confidence = ComputeConfidenceValue(scores, false);
            return result;
        }
    }
}
