Problem Description
You are creating a video player. Your video player supports three functions: move 10 seconds forward, move 10 seconds back, and skip opening. The tasks each function performs are as follows.

Move 10 seconds earlier: When the user enters the "prev" command, the video's playback position is moved 10 seconds before the current location. If the current location is less than 10 seconds, it moves to the initial position of the video. The initial position of the video is 0 minutes 0 seconds.
Move 10 seconds back: When the user enters the "next" command, the video's playback position will be moved 10 seconds from the current location. If the video has less than 10 seconds left, it moves to the last position of the video. The last position of the video matches the length of the video.
Skip Opening: If the current playback position is in the opening segment (≤ ≤ the current playback position), the player automatically moves to the position where the opening ends.op_startop_end
Parameters are given as parameters: a string indicating the length of the video, a string indicating the playback position just before the function is executed, a string indicating the start time of the opening, a string indicating the end time of the opening, and a one-dimensional string array representing the user's input. After all user input is complete, complete the solution function so that the video's position is returned in the format ":".video_lenposop_startop_endcommandsmmss

Restrictions
video_lenLength = Length of = Length of = Length of = Length of = Length of = 5 posop_startop_end
video_len, represent minutes and seconds in the format ":".posop_startop_endmmssmmss
0 ≤ ≤ 59mm
0 ≤ ≤ 59ss
If the minute or candle is a single digit, add zero to represent two digits.
If the current position of the video or the opening end time is outside the video's scope, it will not be provided.
The opening starts at the same time as the opening ends.
1 ≤ Length ≤ 100 commands
commandsThe element is "prev" or "next."
"prev" is a command to move 10 seconds ahead.
"Next" is the command to move 10 seconds back.
Input/Output Example
video_len	pos	op_start	op_end	commands	result
"34:33"	"13:00"	"00:55"	"02:55"	["next", "prev"]	"13:00"
"10:55"	"00:05"	"00:15"	"06:55"	["prev", "next", "next"]	"06:55"
"07:22"	"04:05"	"00:15"	"04:07"	["next"]	"04:17"
Explanation of input/output examples
I/O Example #1

Moving from the starting position 13 minutes 0 seconds to 10 seconds later is 13 minutes 10 seconds.
Moving from 13 minutes 10 seconds to 10 seconds ago becomes 13 minutes 0 seconds.
Therefore, you just need to return "13:00".
I/O Example #2

Move the start position from 0 minutes 5 seconds to 10 seconds ago. Since your current position is less than 10 seconds, you move to 0 minutes 0 seconds.
Moving from 0 minutes 0 seconds to 10 seconds later is 0 minutes 10 seconds.
Moving from 0 minutes 10 seconds to 10 seconds later is 0 minutes 20 seconds. Since 0 minutes 20 seconds marks the opening section, we move to the position where the opening ends, at 6 minutes 55 seconds. Therefore, you can return "06:55".
I/O Example #3

Since the starting position at 4 minutes and 5 seconds marks the opening section, move to the opening end position at 4 minutes 7 seconds. Moving from 4 minutes 7 seconds to 10 seconds later is 4 minutes 17 seconds. Therefore, you just need to return "04:17".