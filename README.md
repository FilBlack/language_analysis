# Language Analysis Tool 

This is a simple tool designed to take a text sample and give the most likely candidates for the language that the text is from. The supported languages are: Czech, Slovak, English, German, Italian, Spanish.


## Algorithm 

A very simple approach would be to scan the input sample for characters that belong exclusively to one of the languages - for example `ß` only appears in German, or `ř` only appears in Czech - but that would be uninteresting. Instead I have chosen languages that all use the Latin alphabet and refrain from using unique characters as an indicator. This can however basically be through in the special case of 1-grams, where the ngrams are the individual characters of the language themselves.

The three methods I have chosen are: 

### ngrams 

A good statistical indicator of a given language is the distribution of character ngrams in a text sample. A 3-gram is any three-letter consecutive substring that appears inside a word, for example `'ing'`, `'the'`, `'est'`. We do not allow spaces to be part of the ngram, so it suffices to split the text into words and extract all ngrams with a sliding window of length n.

Each language has a characteristic fingerprint of which ngrams appear most often. We compare rankings instead of frequencies, because the results would otherwise be heavily squed by the train set. We sort ngrams by how often they appear, keep the top 300, and assign ranks (most frequent = rank 1). Given an unknown input, we build the same ranked profile and measure the total rank displacement against each language profile. The language with the smallest total displacement wins. This is called the Out-of-Place metric.

### Stop-words 

Each language has its own set of stop-words that act as a tell. These are common function words like articles, prepositions and conjunctions. For example Czech uses words like `ano`, `asi`, `ani` very frequently. If we see those words appear often in our sample, there is a high probability that the sample belongs to Czech. These stop-words can also appear in other languages, but at a much lower frequency. We compute the fraction of input tokens that match a language's stop-word list and pick the language with the highest fraction.

### Naive Bayes 

We can also use Bayes' theorem to find the most likely language. In its basic form:

```
P(A | B) = P(B | A) × P(A) / P(B)
```

Applied to our problem we want to maximise P(language | text). Since P(text) is the same for all languages we only need to compare:

```
P(language | text)  ∝  P(text | language) × P(language)
```

The Naive Bayes assumption treats each word as independent, so:

```
P(text | language) = P(word₁ | language) × P(word₂ | language) × ... × P(wordₙ | language)
```

P(word | language) is estimated from training data as the fraction of training words that were this word. P(language) is the fraction of all training words that belong to this language.

Multiplying many small probabilities together causes numerical underflow, so we work in log space and sum log-probabilities instead. We also apply Laplace smoothing - adding 1 to every word count - so that words unseen during training do not immediately give a score of negative infinity.


### Training 

All three analyzers require a training phase, though the stop-word phase is trivial.

For stop-words we simply load a pre-built list of frequent function words for each language and store them in a hash set.

For ngrams and Bayes we need more data, so we use public domain books, one per language. We could download more text to improve accuracy, but training time would grow and the returns diminish quickly with a single book already covering the most frequent patterns.

The training data is cleaned before use: everything except letters and apostrophes is stripped, all text is lowercased, and whitespace is collapsed. Accented characters such as `š`, `č`, `ń` are preserved because they are informative for distinguishing languages.

**ngram training:** For each word in the training text, a sliding window of width n extracts every character ngram. These are counted into a dictionary, sorted by frequency, cut to the top 300, and the counts are replaced by rank numbers. The result is a ranked profile stored per language.

**Bayes training:** Every word in the training text is counted into a per language frequency dictionary. After all languages are processed, the global vocabulary size is measured and log-prior probabilities are computed once and stored.

## Alternative algorithms 

**Word ngrams:** Instead of substrings of characters, use sequences of whole words (bigrams, trigrams). This captures grammatical patterns but requires far more training data since the word vocabulary is much larger than the character vocabulary.

**Dictionary lookup:** Check what fraction of input words appear in a comprehensive dictionary for each language. Simple to implement but fragile - it depends on having a complete dictionary and breaks on proper nouns and unusual words.

**Markov chain character model:** Model the probability of each character conditioned on the previous k characters. This captures more context than independent ngram frequency counts but is essentially a more principled version of what the ngram approach already does.

**Neural models:** Character-level RNNs or transformer models trained on large multilingual corpora. These are the state of the art for language identification and handle very short texts well, but they require orders of magnitude more training data and are overkill for a project of this scope.


## Program structure 

The program is split across four source files. Each has a distinct responsibility.

**TextProcessing.cs**

`TextCleaner` handles all input reading and normalisation.

- `LoadFromFile(path)` - reads a `.txt`, `.pdf` (PdfPig), or `.docx` (OpenXml) file and returns its raw text, throws `NotSupportedException` for any other extension.
- `Clean(raw)` - lowercases the text, replaces everything except letters and apostrophes with a space, and collapses runs of whitespace into single spaces. Accented characters are preserved.
- `Tokenize(cleaned)` - splits a cleaned string on spaces and returns the non-empty parts as a word list.

`TextStats` computes basic statistics (word count, unique words, average length, type-token ratio) from a token list. Currently unused due to clutter.

**Analyzers.cs**

`Languages` is a static class holding the six supported language codes and a `DisplayName(code)` lookup.

`LanguageAnalyzer` is the abstract base for all three detectors.

- `Train(resourcesRoot, languages)` - abstract, each subclass builds its model from the training files.
- `Analyze(cleanedInput)` - abstract, scores every language and returns a sorted `AnalysisResult`.
- `LoadTrainingTexts(resourcesRoot, language)` - reads and cleans all files under `training/<language>/`
- `ComputeConfidenceValue(sortedScores, lowerIsBetter)` - expresses the gap between the winning and runner-up scores as a percentage

`NGramAnalyzer` - sliding-window character ngram detector.

- `Train` - counts all ngrams across the training text, keeps the top 300 by frequency, and replaces counts with ranks (1 = most common) per language.
- `Analyze` - builds the same ranked profile from the input and sums the absolute rank differences against each language profile (Out-of-Place metric), the language with the smallest total wins.
- `GenerateNGrams(cleanedText)` - extracts every n-character substring from each word using a sliding window, public so it can be reused.

`StopWordAnalyzer` - stopword frequency detector.

- `Train` - loads each language's stopword file into a `HashSet<string>`.
- `Analyze` - tokenizes the input and picks the language whose stopword set has the highest hit ratio.

`NaiveBayesAnalyzer` - log-space Naive Bayes classifier.

- `Train` - counts word frequencies per language, measures global vocabulary size, and stores the log-prior for each language.
- `Analyze` - scores each language as `log P(language) + Σ log P(word | language)` with Laplace smoothing (α = 1) to avoid negative infinity on unseen words.

**Evaluation.cs**

`AnalyzerStats` accumulates per language and overall correct/total counts for one analyzer via `Record(trueLanguage, predictedLanguage)`, and exposes `OverallAccuracy()` and `AccuracyForLanguage(language)`.

`ConsensusStats` does the same for the majority vote consensus without the per language breakdown.

`Evaluator` (static) - `LoadSamples(path)` parses a `langcode TAB sentence` TSV file, `Run(samples, analyzers, cleaner)` executes all analyzers on every sample, brings together predictions via majority vote, and prints an overall accuracy table followed by a per language breakdown.

**Program.cs**

`Program` - `Main(args)` parses the command-line flags and dispatches to one of three modes.

- `RunInteractive(cleaner, analyzers)` - reads one line at a time, cleans it, runs all analyzers, and prints each analyzer's prediction, exits on an empty line.
- `RunBatch(cleaner, analyzers, inputPath, outputPath)` - processes each line of the input file, writes per-line predictions with confidence scores and a consensus line to a report file, then appends a summary of analyzer and language agreement statistics.


## User Guide 
The program has three modes: interactive, batch, and evaluation. All modes train the analyzers on startup before doing anything else.


Build:

```
dotnet build
```

### Interactive mode

Type or paste one string at a time and get an immediate prediction from all five analyzers (ngram n=2, ngram n=3, ngram n=4, Stop-word, Naive Bayes). An empty line quits.

```
dotnet run
```

### Batch mode

One sentence per line in the input file. The program runs five analyzers on each line and writes a report file next to the input file.

```
dotnet run -- input.txt
```

### Evaluation mode

Runs five analyzers (ngram n=2, ngram n=3, ngram n=4, Stop-word, Naive Bayes) on every sample in a labelled test file and prints per-analyzer accuracy and a per language breakdown to the console.

The test file must be tab-separated with the language code first: `langcode TAB sentence`.

```
dotnet run -- --eval test\test_data.tsv
```

A pre-built test set with 173 labelled sentences (en, cs, sk, de, es, it) is included in the `test/` folder.


## Data sources

The stop-word lists come from the [stopwords-iso](https://github.com/stopwords-iso) project (MIT licensed).

The training texts are public-domain books:

- en: Pride and Prejudice (Jane Austen) - Project Gutenberg
- cs: R.U.R. (Karel Čapek) - Project Gutenberg
- de: Die Verwandlung (Franz Kafka) - Project Gutenberg
- es: Don Quijote (Miguel de Cervantes) - Project Gutenberg
- it: La Divina Commedia (Dante Alighieri) - Project Gutenberg
- sk: Prostonárodné slovenské povesti (Pavol Dobšinský) - Slovak Wikisource


## What is missing 

The CLI is very crude and the visual aspect of it could be improved a lot.

Through the nature of this project the code is infinitely expandable - you can simply add another analyzer and aggregate its results into the consensus. I felt that three methods of analysis would be enough and I believe I was proven right by the test data: the consensus method achieved around 95% accuracy on the 173-sentence test set, beating each individual analyzer.

I could have found more books to increase the training set size and possibly improved the accuracy of the ngram methods for n=3 and n=4.

If I had gathered more data I could have used a word-based bigram method that counts the frequency of pairs of consecutive words, but this requires a much larger corpus and the returns over character ngrams are not obvious for language detection specifically.

The consensus model could be improved by weighting each analyzer's vote by its confidence score, or by taking 2nd and 3rd choices into account rather than just the top prediction.


## My work on the project 

I initially thought that a basic ngram, stop-word and Bayes implementation with data cleaning and a CLI interface would be enough to meet the criteria of the project, but after seeing how compact the initial implementation was, it pushed me to improve the program significantly and I am grateful for that.

For some reason I did not think of including a proper labelled test set at the start. I assumed that being able to paste text and see a result would be sufficient, but that is pointless if you cannot quantify how much to trust the output.


## Final remark 

I like very theoretical projects, but the issue is that the code length of each of them tends to be rather small and the amount of thought that goes into the specific algorithms does not show in the code. Most of the volume comes from wrappers that parse input and interact with the user, which I personally dislike writing.

Nonetheless I enjoyed working on the project and learning the theory behind language analysis.

It was interesting to see the results of the tests. I was expecting Bayes to underperform due to the small training set size, but it actually performed the best across all methods. I was also happy to see that the consensus method beat all individual analyzers.

The largest surprise was that the ngram analysis works best for n=2. This may be a consequence of the small training set - shorter ngrams appear more often and produce more reliable frequency estimates from limited data - and this is a potential area of exploration.

My hypothesis was that Slovak and Czech would be the hardest pair to distinguish and it was proven correct. I also thought Spanish and Italian might be frequently confused with each other but that turned out not to be the case.

Disclosure: I used LLMs for data gathering (downloading test and training data from the internet) as I believe this manual work would not have taught me much. I also used them to improve the documentation formatting.
