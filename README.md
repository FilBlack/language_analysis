# Language Analysis Tool 

This is a simple tool designed to take a text sample and give the most likely candidates for the language that the text is from. The supported languages are: Czech, Slovak, English, German, Italian, Spanish.


## Algorithm 

A very simple approach would be to scan the input sample for characters that belong exclusively to one of the languages — for example `ß` only appears in German, or `ř` only appears in Czech — but that would be uninteresting. Instead I have chosen languages that all use the Latin alphabet and refrain from using unique characters as an indicator. This can however basically be through in the special case of 1-grams, where the n-grams are the individual characters of the language themselves.

The three methods I have chosen are: 

### N-grams 

A good statistical indicator of a given language is the distribution of character n-grams in a text sample. A 3-gram is any three-letter consecutive substring that appears inside a word, for example `'ing'`, `'the'`, `'est'`. We do not allow spaces to be part of the n-gram, so it suffices to split the text into words and extract all n-grams with a sliding window of length n.

Each language has a characteristic fingerprint of which n-grams appear most often. We compare rankings instead of frequencies, because the results would otherwise be heavily squed by the train set. We sort n-grams by how often they appear, keep the top 300, and assign ranks (most frequent = rank 1). Given an unknown input, we build the same ranked profile and measure the total rank displacement against each language profile. The language with the smallest total displacement wins. This is called the Out-of-Place metric.

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

Multiplying many small probabilities together causes numerical underflow, so we work in log space and sum log-probabilities instead. We also apply Laplace smoothing — adding 1 to every word count — so that words unseen during training do not immediately give a score of negative infinity.


### Training 

All three analyzers require a training phase, though the stop-word phase is trivial.

For stop-words we simply load a pre-built list of frequent function words for each language and store them in a hash set.

For n-grams and Bayes we need more data, so we use public domain books, one per language. We could download more text to improve accuracy, but training time would grow and the returns diminish quickly with a single book already covering the most frequent patterns.

The training data is cleaned before use: everything except letters and apostrophes is stripped, all text is lowercased, and whitespace is collapsed. Accented characters such as `š`, `č`, `ń` are preserved because they are informative for distinguishing languages.

**N-gram training:** For each word in the training text, a sliding window of width n extracts every character n-gram. These are counted into a dictionary, sorted by frequency, cut to the top 300, and the counts are replaced by rank numbers. The result is a ranked profile stored per language.

**Bayes training:** Every word in the training text is counted into a per-language frequency dictionary. After all languages are processed, the global vocabulary size is measured and log-prior probabilities are computed once and stored.


## Alternative algorithms 

**Word n-grams:** Instead of substrings of characters, use sequences of whole words (bigrams, trigrams). This captures grammatical patterns but requires far more training data since the word vocabulary is much larger than the character vocabulary.

**Dictionary lookup:** Check what fraction of input words appear in a comprehensive dictionary for each language. Simple to implement but fragile — it depends on having a complete dictionary and breaks on proper nouns and unusual words.

**Markov chain character model:** Model the probability of each character conditioned on the previous k characters. This captures more context than independent n-gram frequency counts but is essentially a more principled version of what the n-gram approach already does.

**Neural models:** Character-level RNNs or transformer models trained on large multilingual corpora. These are the state of the art for language identification and handle very short texts well, but they require orders of magnitude more training data and are overkill for a project of this scope.


## Program structure 

The program is split across four source files. Each has a distinct responsibility.

**TextProcessing.cs**

`TextCleaner` handles all input reading and normalisation. It can load `.txt`, `.pdf` and `.docx` files using the PdfPig and OpenXml libraries, and exposes a `Clean` method that strips everything except letters and apostrophes, lowercases the result, and collapses whitespace. All three analyzers pass their training files through this class before doing any counting. `Tokenize` splits a cleaned string into a word list for the analyzers that work with words.

`TextStats` computes basic statistics about a tokenized text — word count, unique word count, average word length, and type-token ratio. These are written to the batch report and have no effect on the predictions. Currently not used because of clutter.

**Analyzers.cs**

`Languages` is a static class that holds the list of supported language codes and maps them to display names.

`LanguageAnalyzer` is the abstract base class for all three detectors. It provides `LoadTrainingTexts` (which finds and cleans all training files for one language), and `ComputeConfidenceValue` (which measures how far ahead the winning language is from the runner-up and expresses this as a percentage).

`NGramAnalyzer` implements the character sliding window, profile building and Out-of-Place distance scoring. It keeps a trained ranked profile per language in a dictionary of dictionaries.

`StopWordAnalyzer` loads per-language stop-word lists into hash sets during training and computes the hit ratio on each analysis call.

`NaiveBayesAnalyzer` counts word frequencies per language during training, computes log-priors, and scores each input as the sum of the log-prior and all per-word log-likelihoods with Laplace smoothing.

**Evaluation.cs**

`Evaluator` loads a tab-separated labelled test file, runs all analyzers on every sample, and prints an accuracy table broken down by analyzer and by language. `AnalyzerStats` and `ConsensusStats` accumulate the per-language and overall correct/total counts.

**Program.cs**

`Program` parses command-line arguments in a simple loop and chooses one of three modes to use: interactive (prompt loop), batch (reads an input file, writes a report), or evaluation. The batch mode writes a report containing each analyzer's prediction with confidence score, a consensus line, and a summary with per-analyzer and per-language agreement statistics.


## User Guide 
The program has three modes: interactive, batch, and evaluation. All modes train the analyzers on startup before doing anything else.


Build:

```
dotnet build
```

### Interactive mode

Type or paste one string at a time and get an immediate prediction from all five analyzers (N-gram n=2, N-gram n=3, N-gram n=4, Stop-word, Naive Bayes). An empty line quits.

```
dotnet run
```

### Batch mode

One sentence per line in the input file. The program runs five analyzers on each line and writes a report file next to the input file.

```
dotnet run -- input.txt
```

### Evaluation mode

Runs five analyzers (N-gram n=2, N-gram n=3, N-gram n=4, Stop-word, Naive Bayes) on every sample in a labelled test file and prints per-analyzer accuracy and a per-language breakdown to the console.

The test file must be tab-separated with the language code first: `langcode TAB sentence`.

```
dotnet run -- --eval test\test_data.tsv
```

A pre-built test set with 173 labelled sentences (en, cs, sk, de, es, it) is included in the `test/` folder.


## Data sources

The stop-word lists come from the [stopwords-iso](https://github.com/stopwords-iso) project (MIT licensed).

The training texts are public-domain books:

- en: Pride and Prejudice (Jane Austen) — Project Gutenberg
- cs: R.U.R. (Karel Čapek) — Project Gutenberg
- de: Die Verwandlung (Franz Kafka) — Project Gutenberg
- es: Don Quijote (Miguel de Cervantes) — Project Gutenberg
- it: La Divina Commedia (Dante Alighieri) — Project Gutenberg
- sk: Prostonárodné slovenské povesti (Pavol Dobšinský) — Slovak Wikisource


## What is missing 

The CLI is very crude and the visual aspect of it could be improved a lot.

Through the nature of this project the code is infinitely expandable — you can simply add another analyzer and aggregate its results into the consensus. I felt that three methods of analysis would be enough and I believe I was proven right by the test data: the consensus method achieved around 95% accuracy on the 173-sentence test set, beating each individual analyzer.

I could have found more books to increase the training set size and possibly improved the accuracy of the n-gram methods for n=3 and n=4.

If I had gathered more data I could have used a word-based bigram method that counts the frequency of pairs of consecutive words, but this requires a much larger corpus and the returns over character n-grams are not obvious for language detection specifically.

The consensus model could be improved by weighting each analyzer's vote by its confidence score, or by taking 2nd and 3rd choices into account rather than just the top prediction.


## My work on the project 

I initially thought that a basic n-gram, stop-word and Bayes implementation with data cleaning and a CLI interface would be enough to meet the criteria of the project, but after seeing how compact the initial implementation was, it pushed me to improve the program significantly and I am grateful for that.

For some reason I did not think of including a proper labelled test set at the start. I assumed that being able to paste text and see a result would be sufficient, but that is pointless if you cannot quantify how much to trust the output.


## Final remark 

I like very theoretical projects, but the issue is that the code length of each of them tends to be rather small and the amount of thought that goes into the specific algorithms does not show in the code. Most of the volume comes from wrappers that parse input and interact with the user, which I personally dislike writing.

Nonetheless I enjoyed working on the project and learning the theory behind language analysis.

It was interesting to see the results of the tests. I was expecting Bayes to underperform due to the small training set size, but it actually performed the best across all methods. I was also happy to see that the consensus method beat all individual analyzers.

The largest surprise was that the n-gram analysis works best for n=2. This may be a consequence of the small training set — shorter n-grams appear more often and produce more reliable frequency estimates from limited data — and this is a potential area of exploration.

My hypothesis was that Slovak and Czech would be the hardest pair to distinguish and it was proven correct. I also thought Spanish and Italian might be frequently confused with each other but that turned out not to be the case.

Disclosure: I used LLMs for data gathering (downloading test and training data from the internet) as I believe this manual work would not have taught me much. I also used them to improve the documentation formatting.
