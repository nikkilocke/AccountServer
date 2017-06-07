# Installation

## Windows

Either:

* Run the provided AccountServerSetup.msi program. This will install AccountServer into the folder of your choice (default C:\Program Files), and create shortcuts to it on your Desktop and Start menu.

Or:

* Extract all the files from the zip file into a folder of your choice.
* Open a command prompt as administrator (in your start menu, search for "CMD", right click it, and choose "Run as Administrator").
* In the command prompt, register the port AccountServer uses - type <sup id="a1">[*](#f1)</sup> **netsh http add urlacl url=http://+:8080/ user=Everyone**
* Close the command prompt.
* Open Windows Explorer and navigate to your chosen folder.
* If you wish to set up a shortcut in the start menu, right click on AccountServer.exe and choose "Pin to Start".
* Start the program by double-clicking on it (or on your shortcut).
* The program will create a SQLite database in C:\ProgramData\AccountServer, start up, and open your web browser at the Company page.

<b id="f1">*</b> The **8080** here is the port on which the web server will listen. You can change this if you like, but you must also include a **Port=** line in the config file <sup id="a1">[*](#f3)</sup>.

## Linux

* Extract all the files from the zip file into a folder of your choice.
* Start the program by double-clicking on it.
* The program will create an SQLite database un /usr/share/AccountServer and start up.
* Open your web browser, and navigate to <sup id="a2">[*](#f1)</sup> **http://localhost:8080/**

<b id="f2">*</b> The 8080 here is the port on which the web server is listening.

## Using a MySql Database

If you wish to use a MySql database instead of SQLite, stop the program and:

* Create an empty database in your MySql server (e.g. **accountsdb**).
* Create a new user (or use an existing user) who can connect from your machine (e.g. **accountsuser**, password **accountspassword**).
* Give that user full permissions on the database you just created.
* Edit the AccountServer.config file <sup id="a1">[*](#f3)</sup>, and change the following items:
  * "Database": "MySql",
  * "ConnectionString":"server=**localhost**;user=**accountsuser**;database=**accountsdb**;port=3306;password=**accountspassword**",

(Fill in the bold fields in the connection string above according to how you set up the database, replacing **localhost** with the machine running MySql if it is running elsewhere on your network.)

<b id="f3">*</b> The config file is plain text in json format, so you can edit it with any text editor, e.g. Notepad (which comes with Windows). In Windows it is stored in c:\ProgramData\AccountServer. In Linux it is in /usr/share/AccountServer.

