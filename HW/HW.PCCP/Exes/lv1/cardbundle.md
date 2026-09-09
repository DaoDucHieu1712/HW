Problem Description
Connie received two decks of cards with English words as a gift. Connie wants to know if he can use the words written on the cards to create a word arrangement in the desired order according to the following rules.

Use cards one by one in the desired deck in order.
Once a card is used, it cannot be used again.
You cannot move on to the next card without using a card.
The word order of an existing deck of cards cannot be changed.
For example, if the first deck has ["i", "drink", "water"] in order, and the second deck has ["want", "to"] in order, and you want to create a word array in the order ["i", "want", "to", "drink", "water"], you can use "i" on the first deck, then "want" and "to" on the second, and "drink" and "water" in sequence on the first deck, to create the word arrangement in the desired order.

An array made of strings, and the desired word array. When given these parameters, if you create the words written in and , return "Yes"; if not, return "No".cards1cards2goalcards1cards2goal

Restrictions
1 ≤ length, length of ≤ 10 cards1cards2
1 ≤ length, length of ≤ 10cards1[i]cards2[i]
cards1Only different words exist in and .cards2
2 Length of ≤ ≤ length + length of goalcards1cards2
1 ≤ length ≤ 10goal[i]
goalThe elements of are made up only of the elements of and .cards1cards2
cards1The strings of , , are all composed only of lowercase letters.cards2goal
Input/Output Example
cards1	cards2	goal	result
["i", "drink", "water"]	["want", "to"]	["i", "want", "to", "drink", "water"]	"Yes"
["i", "water", "drink"]	["want", "to"]	["i", "want", "to", "drink", "water"]	"No"
Explanation of input/output examples
I/O Example #1

Same as the main text.

I/O Example #2

cards1You can use "i" in and "want" and "to" in to make "i want to," but since "water" must be used before "drink," you cannot complete that sentence. Therefore, it returns "No."cards2