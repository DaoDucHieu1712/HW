Problem Description
KakaoTalk offers a spoiler prevention feature that hides part of the message and only reveals it when clicked. This feature allows important information to be concealed and sent off.

Muji used this feature to apply spoiler prevention throughout a single message and sent it to you. You want to check how important the words are among the revealed words, by clicking the spoiler prevention section one by one from the start of the message from left to right → right.

Word and Important Word Rules

Words are separated by spaces and consist of continuous strings composed only of lowercase letters and numbers.
If one or more of the indices of the characters that make up a word fall within the spoiler protection zone, the word is considered spoiler-protected. In other words, even if spoiler protection is applied to only some characters within a word, the entire word is considered spoiler-protected.
A single word can span multiple no-sprue sections, and a single no-spred section can contain multiple words.
When you click the spoiler prevention section and all characters of a word are exposed, if the word meets all of the following conditions, it is considered important.
It must be a word to prevent spoilers.
It must not have appeared in any non-spoiler protection section of the message (= all sections not included in any such zone: including the front, middle, and back of each section).
It must not duplicate previously released spoiler prevention words.
If multiple words are revealed simultaneously, determine which words are important from left to right, one by one.
When a string representing a message from ignorance and a two-dimensional integer array representing the anti-spoiler interval are given as parameters, complete the solution function to return the number of important words among the spoiler-protected words.messagespoiler_ranges

Restrictions
Length of 1 ≤ ≤ 20,000 message
messageconsists of lowercase letters, numbers, and spaces.
messageis a string composed of one or more words.
Spaces do not appear consecutively.
Length of 1 ≤ ≤ 1,000 spoiler_ranges
spoiler_ranges[i]indicates the section with spoiler prevention applied in the form of [, ] Here, and are character indices, and both indices are included in the interval.startendstartend
Length of 0 ≤ ≤ <startendmessage
All segments do not overlap and are arranged in ascending order based on the reference.start
Test Case Configuration Guide
Below is the configuration of the test case. Each group consists of one or more subgroups, and by passing all test cases in the subgroup, you earn the assigned points for that group.

Group	Total score	Additional Restrictions
#1	7%	messageEvery word appears only once, without repetition.
#2	13%	Every anti-spoiler section points exactly to the beginning and end of a single word. Length = 1spoiler_ranges
#3	45%	Every anti-spoiler section points exactly to the beginning and end of a single word.
#4	35%	No additional restrictions
Input/Output Example
message	spoiler_ranges	result
"here is muzi here is a secret message"	[[0, 3], [23, 28]]	1
"my phone number is 01012345678 and may i have your phone number"	[[5, 5], [25, 28], [34, 40], [53, 59]]	4
Explanation of input/output examples
I/O Example #1

ChatGPT Image 2025년 9월 9일 오후 07_20_38 (3).png
This is a spoiler-proof chat.

ChatGPT Image 2025년 9월 9일 오후 07_20_38 (1).png
This is the first chat with spoiler protection disabled. When you disable the first anti-spoiler section, the word here is revealed. Here has appeared in areas that are not spoiler-protected. So it's not an important word.

ChatGPT Image 2025년 9월 9일 오후 07_20_38.png
This is the second chat with spoiler protection disabled. Unlocking the second spoiler prevention section reveals the word secret. 'secret' is an important word.
Since only one important word is 'secret', you need to return 1.

I/O Example #2

ChatGPT Image 2025년 9월 10일 오전 12_48_32 (6).png
This is a spoiler-proof chat.

ChatGPT Image 2025년 9월 10일 오전 12_48_32 (5).png
This is the first chat with spoiler protection disabled. When you unlock the first spoiler segment, the word phone is revealed. This word has never appeared in a non-spoiler section and does not overlap with previously released pre-release warnings, making it an important word.

ChatGPT Image 2025년 9월 10일 오전 12_48_32 (4).png
This is the second chat with spoiler protection disabled. Unlocking the second spoiler section reveals the word 01012345678. This word is important.

ChatGPT Image 2025년 9월 10일 오전 12_48_32 (3).png

Third, this is chat with spoiler protection disabled. When you unlock the third spoiler section, the words 'may' and 'i' are revealed. Both words are important.

ChatGPT Image 2025년 9월 10일 오전 12_48_32.png

This is the last post-war chat with spoiler protection disabled. When you unlock the last spook section, your phone and number will be revealed. Among these, phone is included in previously released spoiler prevention words, and number has been disclosed in areas outside the scope of the pre-spoiler zone. So it's not an important word.

Among the words with spoiler protection, the important ones are phone, 01012345678, may, and i. Therefore, 4 must be returned.