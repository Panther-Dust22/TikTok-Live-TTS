Note the BSR injector is no longer bundled with this system
get it from here https://github.com/Panther-Dust22/TikTok-Beatsaber-bsr-Injector

![Header Banner](Images/banner.png)


:---:
```
✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨
     ✨         TTS Voice System V4.6            ✨
     ✨                OVERHAUL                  ✨
     ✨       ___ __  __    ___ ________         ✨
     ✨      | __|  \/  |/\|_  )__ /__ /         ✨
     ✨      | _|| |\/| >  </ / |_ \|_ \         ✨
     ✨      |___|_|  |_|\//___|___/___/         ✨
     ✨💫Created by Emstar233 & Husband (V4.6)💫✨
✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨⭐✨
```  


Thanks for downloading the new Overhauled version of out TTS

We have tried to make this as user friendly as possible,
but there may still be glitches and bugs, feel free to report these

you can discuss this program and its features or code on our Discord

               https://discord.gg/PVvv8M5e83

we are always active and ready to help you with any issues.

             \*\*..................Why did we make this?...............…\*\*


Originally Emstar wanted to be able to accept bsr codes in Beatsaber while streaming on TikTok,
she asked and begged and annoyed her Husband until he gave in and built a bypass,
program that passes all messages from TikTok to Twitch, this started off in Streamer.Bot
it worked but it was unreliable so he built a version 2 using an internet browser and some JS
then she discovered TTS and wanted her own, so using some code found online he built one for her
this was version 2, then with some tweaks and added extras like more voices, mod commands and so on
he built version 2.5 and released it to everyone on GitHub.

Not being happy with editing txt files to change settings and the complexity of the setup,
the people began to form a mob with pitchforks and fire, they would scream and shout for an update,
but not as loud as Emstar, so he created v3 that didn't get to see a release, that cleaned up the
setup process and the settings issue, Emstar was silenced but the people still roared for an update.

with a week to spare and a laptop strapped to his face Husband began to code, hours went past,
slowly turning to days, and then a week had arrived, with a test stream behind him and bugs
found that needed to be squashed he set aside a couple extra days and
handcuffed himself to the sofa and laptop.

2 days pass with 2 and 3AM bed times and it was finally here, with the next day spent tweaking,
Version 4, a complete overhaul, A rustic user interface, automatic moderator commands, simple,
elegant, idiot proof, a shining beacon of what someone that cant code for shit can do, turning no skill into....



                  **.....The TTS BSR Injector V4 Overhaul edition....**

Install instructions.

**Previous bsr tts users**
copy all your old txt files and put them in this folder with the installer and convert file
Run Install which will make sure you have the correct Python version and all the requirements needed for this project
run the convert and create file (this will transform your old txt files to the new format and then put them away safely)
double click the new Run TTS BSR icon to start the GUI, you can copy this to your desktop if needed (right click - send to - desktop create shortcut)
Add plugins and user data from the BSR Injector Plugin folder to your game directory

DONE!

**New Users**
Install Tikfinity https://tikfinity.zerody.one/app/
Run the Install file
run the Convert and Create file (for you this will create all the files needed to run this project)
double click the new Run TTS BSR icon to start the GUI, you can copy this to your desktop if needed (right click - send to - desktop create shortcut)
Add plugins and user data from the BSR Injector Plugin folder to your game directory

DONE!

For Commands in BeatSaber+ see Beatsaber+ docs

                    \*\*..................In the GUI...............…\*\*




In the GUI are all the buttons, bells and whistles (voices) that you could need.
The left side gives you a command window, an idea of what is going on in the program.



on the right side 1st row you have a row of buttons.



Start/Stop    : will be green when TTS is running and Red when stopped

!tts command  : Enables or disables the need for viewers to use the !tts code to speak

Mod Commands  : Enables or disables the abilities for Moderators to control your TTS NEW COMMANDS ADDED SEE END OF README!

Specials      : A control panel for default tts speed, default voices for mods, subs followers etc

Edit bad reply: Brings up a panel to add or remove filter words and replies
Emergency stop: Turn on or off the Mod command to stop tts and reset it if someone is being a prick

** Not Needed anymore - left as a backup option **
BSR Injector  : Opens the improved bsr injector page for sending everything to twitch to make the bsr codes work


2nd row is active user control, active users in the last 10 mins are captured and added to this list for voice control


Active user       : Select the user to adjust
Voices            : Select a voice available from the list for this user
Name Swap         : change a users spoken name (great if they have a long name or use emojis leave empty for no name swap)
Speed             : Change the spoken speed of this one user (none uses set default speed)
Apply (button)    : Apply the above changes
Remove (button)   : Removes the above properties for selected user
Edit List (button): Shows your entire priority voice list where you can delete users off even if not online

3rd row is volume

Volume slider     : Do i have to explain this one?

4th row is updates and test API

Updates button    : will flash red if there is an update available on Github for you
Test API          : Will check the API is working for voices, mostly just use to diagnose issues

5th row is my information bar, shows next planned update and gives a link to our discord

HAVE FUN!

Updates: Installer was having issues so I fixed it for you to manualy install Python, it will give you a clickable link, Crash Detector no longer randomly shuts down and makes sure that you are always running!
New Official BSR plugin for TikTok added, have fun with this streamers.

Mod commands for stream if enabled

| Command                  | Format                          | Description                                                     | Example                        |
| ------------------------ | ------------------------------- | --------------------------------------------------------------- | ------------------------------ |
| \*\*Add Voice Mapping\*\*    | `!vadd <name> VOICE \[speed]`    | Assign a specific TTS voice (and optional speed) to a username. | `!vadd John Smith DITCH 1.2`   |
| \*\*Remove Voice Mapping\*\* | `!vremove <name>`               | Remove a stored voice mapping for the user.                     | `!vremove John Smith`          |
| \*\*Change Voice Mapping\*\* | `!vchange <name> VOICE \[speed]` | Update an existing mapping to a new voice (and optional speed). | `!vchange John Smith HILLARY`  |
| \*\*Add Name Swap\*\*        | `!vname <original> - <new>`     | Change how a user’s name is spoken in TTS.                      | `!vname John Smith - The King` |
| \*\*Remove Name Swap\*\*     | `!vnoname <name>`               | Remove a spoken name override.                                  | `!vnoname John Smith`          |
| \*\*Add Rude Words\*\*       | `!vrude <word1> <word2> ...`    | Add words to the rude-word filter. Avoids duplicates.           | `!vrude foo bar baz`           |
| \*\*Emergency shut down\*\*  | `!restart`                      | If enabled in the GUI stops and restarts TTS (clears queue).    | `!restart          `           |

