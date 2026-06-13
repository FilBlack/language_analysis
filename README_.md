# Language Analysis Tool 

This is a simple tool designed to take a text sample and give probability scores for the language that the text is most likely from. The supported languages are: czech, slovak, english, german, italian, spanish. 


## Algorithm 

A very simple approach would be to scan the input sample for characters that belong exclusively to one of the languages, INSERT EXAMPLE HERE, but that would be uninteresting, therefore I have chosen languages that all use the (lating character set ) - USE THE CORRECT TERM HERE, and refrain from using the characters themselves as an indicator. This can however basically be used in the special case of 1-grams that are the characters of the language themselves. 



The three methods I have chosen are: 

### Ngrams 
A good statistical indicator of a given language is the appearance of ngrams in a sample text. For example a 3-gram is 'ano', 'bat', 'gre' and any other three leter consecture string that appears in a word of the language. We do not allow spaces to be a part of the ngram, therefore it suffices to split the text into words and create the ngrams with a sliding window of lenght n. 

### Stopwords 
Each language has its own set of stopwords that act as a tell. For example the czech language uses 'ano', 'asi', 'ani' very frequently, which means that if wee see those words appear frequently in our sample, there is a high probability that our sample belongs to the given language. These stopwords can also appear in other languages, but at a much lower probability.

### Naive Bayes Analyzer 
We can also use the Byase theorem to gauge the probability that a sample belongs to a language.  INSERT normal BAYES formula.  Taking this for our case, we can calculate P(language|sample) INSERT more complicated formula here. 



#### Training 
All three analyzers require a training phase, albeit the stopword training phase is trivial. 
For stopwords we simply download a cleaned set of frequent stopwords for each language and treat that as our stopword model. 
For ngrams and Bayes, we require more data, so we turn to public sources. We downloaded a single representative book for each language as our training dataset. We could have downloaded more text to improve the accuracy of the models, but that would increase training time and there would be diminishing returns on the accuracy of the models. 

Once we have our books, we need to clean the data using a data cleaner. In our case the data cleaner simply consists of methods that remove irrelevant characters, transform all words to lowercase and remove excessive whitespace CORRECT THIS HERE IF ITS WRONG, MAYBE WE PASS A DICT FROM THE CLEANER ALREADY. 

Once we have our cleaned up data, we train our models: 

Ngram:
   We create a sliding window and for each word in the trainset we gather the ngrams that form subsets of the word itself. We then store these ngrams in a dictionary that holds each ngram as the key and the absolute frequency as the value. After we are done we are left with an approximation of the frequency of each ngram in a given language. Dividing each ngram value by the total number of ngrams gives us the probability that an ngram we found in a sample text is this ngram, give that the sample is in our language. 


Bayes: 

I DO NOT KNOW - EXPLAIN HERE

## Alternative algorithm 

   FILL THIS WITH OTHER METHODS FOR LANGUAGE DETECTION AND I WILL ELABORATE ON THEM

## Program structure 
 The Program creates these key objects:
   Languages 
      stores the supported languages and some simple information about them 
   TextCleaner
       used to create LanguageAnalyzer that then utilizes it during training 
   LanguageAnalyzer
      An abstract class that is inherited by each type of analyzer 
      These classes are 
         Ngram Analyzer
         Stopword Analyzer
         Bayse analyzer 
   CliOptions 
      used parse user interactions with the cli 

THIS WILL NEED TO BE SEVERELY EXPANDED, I will do this myself




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

One sentence per line in the input file. The program runs three analyzers (N-gram n=3, Stop-word, Naive Bayes) on each line and writes a report file next to the input file.

```
dotnet run -- input.txt
```

Change the n-gram size used by the N-gram analyzer (default is 3):

```
dotnet run -- input.txt --ngram 2
dotnet run -- input.txt --ngram 4
```

### Evaluation mode

Runs five analyzers (N-gram n=2, N-gram n=3, N-gram n=4, Stop-word, Naive Bayes) on every sample in a labelled test file and prints per-analyzer accuracy and a per-language breakdown to the console.

The test file must be tab-separated with the language code first: `langcode TAB sentence`.

```
dotnet run -- --eval test\test_data.tsv
```

A pre-built test set with 173 labelled sentences (en, cs, sk, de, es, it) is included in the `test/` folder.
## Data sources

The stop-word lists come from the [stopwords-iso](https://github.com/stopwords-iso)
project (MIT licensed).

The training texts are public-domain books from
[Project Gutenberg](https://www.gutenberg.org/):

- en: Pride and Prejudice (Jane Austen)
- cs: R.U.R. (Karel Čapek)
- de: Die Verwandlung (Franz Kafka)
- es: Don Quijote (Miguel de Cervantes)
- it: La Divina Commedia (Dante Alighieri)
- sk: Prostonarodni povesti 



## What is missing 

The ClI is very crude and the visual aspect of it could be improved a lot.
Through the nature of this project the code is infinitely expandable, you can simply add another analyzer and aggregate the results from it into the analysis, I felt that three methods of analysis would be enough and I believe I was proven right by the test data: INSERT TEST RESULT FOR THE TEST SET.
I could have found more books to increase the train set size and possibly increased the accuracy of the ngram methods for n=3,4 
If I also gathered more data I could have used the word based bigram method that would count the frequency of two words appearing after each other, but it seems excessive and weird to me, just found this on the internet. 
The consensus model could be upgraded by taking into account the specific knowledge the analyzers have on their own or taking into account 2nd and 3rd choices of the analyzer. 

## My work on the project 
I initially though that a very simple ngram, stopword and bayes analysis with data cleaning and a cli interface would be enough to meet the criteria of the project but after discovering that it only took INSERT THE NUMBER OF LINES IT TOOK, it pushed me to improve the program a lot more and I am grateful for that. 
For some reason at the start I did not even think of including a proper test set to test all the individual analyzers. I thought that simply being able to paste text would be enough but that is kind of pointless if you cant trust the results. 


## Final remark 
I like very theoretical projects, but the issue is that the code length of each of them tends to be rather small and the amount of though that goes into the specific algorithms does not show in the code. Most of the volume comes from wrappers that are able to parse the input and interact with the user, which I personally dislike writing. 
Nonetheless I enjoyed working on the project and learning the theory behind language analysis. 
It was interesting to see the results of the tests, I was expecting bayes to fail due to the small trainset size, but it actually performed the best accross all methods. 
I was happy to se that the consensus method beat all individual analyzers.
The largest shocker was that the ngram analysis works best for n=2. That may just be due to the small trainset size and this is a potential area of exploration for the future. 
My hypothesis was that slovak and czech would be the hardest to compare and it was proven correct. I also thought that maybe spanish and italian would be mixed up a lot but that turned out not to be the case. 

Disclosure: I used LLMs for the data gathering (downloading test and train data from the internet) as I believe this manual work would not have taught me much. 
I did also use them to enhance the visual aspect of the documentation and here I admit I was lazy and I should have gained better markdown skills on my own.   