Problem Description
There is a parcel box with a number of 1 ~ in the warehouse. You organized the delivery boxes as follows.n

Move from left to right and place the delivery boxes one by one in the order of number from box 1. If you placed the parcel boxes horizontally, this time you place one parcel box on each floor from right to left. If you place a box on that floor and return to the far left, go from left to right and place the box on the upper floor. In this way, stack each box on one floor until all the delivery boxes are placed.wwnw

ex1-1.png

The diagram above shows an example of stacking 22 delivery boxes at = 6.w
The next day, the customer came to the warehouse to pick up their package. When a guest says their parcel box number, you take out the box. To take out parcel box A, you must first remove all other boxes above A to retrieve A. For example, in the picture above, to take out box 8, you must first take boxes 20 and 17.

When you are given the box number to take out, you want to know how many delivery boxes you need to take out in total, including the boxes you want to take.

An integer representing the number of parcel boxes in the warehouse, an integer representing the number of boxes placed horizontally, and an integer representing the number of parcel boxes to be retrieved are given as parameters. At this point, complete the solution function so that the total number of boxes to be removed is returned.nwnum

Restrictions
2 ≤ ≤ 100n
1 ≤ ≤ 10w
1 ≤ ≤ numn
Test Case Configuration Guide
Below is the configuration of the test case. Passing all test cases within each group earns the points assigned to that group.

Group	Total score	Additional Restrictions
#1	10%	w = 1
#2	20%	n is a multiple of w
#3	70%	No additional restrictions
Input/Output Example
n	w	num	result
22	6	8	3
13	3	6	4
Explanation of input/output examples
I/O Example #1

Here is an example of the problem. There are three boxes you need to take, including Box 8.

I/O Example #2

ex2-1.png

To get chest 6, you must first take boxes 13, 12, and 7.
Therefore, return 4.