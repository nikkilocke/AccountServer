#AccountServer Accounting Software

##Installation

###Windows

* Extract all the files from the zip file into a folder of your choice.
* Open a command prompt as administrator (in your start menu, search for "CMD", right click it, and choose "Run as Administrator").
* In the command prompt, register the port AccountServer uses - type <sup id="a1">[*](#f1)</sup> **netsh http add urlacl url=http://+:8080/ user=Everyone**
* Close the command prompt.
* Open Windows Explorer and navigate to your chosen folder.
* If you wish to set up a shortcut in the start menu, right click on AccountServer.exe and choose "Pin to Start".
* Start the program by double-clicking on it (or on your shortcut).
* The program will create an SQLite database, start up, and open your web browser at the Company page.

<b id="f1">*</b> The **8080** here is the port on which the web server will listen. You can change this if you like, but you must also include a **Port=** line in the config file.[?](#a1)

###Linux

* Extract all the files from the zip file into a folder of your choice.
* Start the program by double-clicking on it.
* The program will create an SQLite database and start up.
* Open your web browser, and navigate to <sup id="a2">[*](#f1)</sup> **http://localhost:8080/**

<b id="f2">*</b> The 8080 here is the port on which the web server is listening.[?](#a2)

###Using a MySql Database

If you wish to use a MySql database instead of SQLite, stop the program and:

* Create an empty database in your MySql server (e.g. **accountsdb**).
* Create a new user (or use an existing user) who can connect from your machine (e.g. **accountsuser**, password **accountspassword**).
* Give that user full permissions on the database you just created.
* Edit the AccountServer.config file <sup id="a1">[*](#f3)</sup>, and change the following items:
  * "Database": "MySql",
  * "ConnectionString":"server=**localhost**;user=**accountsuser**;database=**accountsdb**;port=3306;password=**accountspassword**",

(Fill in the bold fields in the connection string above according to how you set up the database, replacing **localhost** with the machine running MySql if it is running elsewhere on your network.)

<b id="f3">*</b> The config file is plain text in json format, so you can edit it with any text editor, e.g. Notepad (which comes with Windows).[?](#a3)

##Importing Quick Books data

Importing is on the Admin menu.

Quick books does not show the full name of sub-accounts in its reports. It is therefore essential that you edit any subaccounts to give them unique names. E.g. rename the Taxes subaccount under Payroll to Payroll/Taxes.

To transfer data from Quick Books, you only need 2 files.

Use File, Utilites, Export to produce an IIF File - if you tick all the boxes, you can create a single file containing most of your data. The only other data you will need is a Custom Transaction Detail Report for the transactions. Customize the report, make sure all the fields listed below are selected, select All dates, run the report and then print it to a tab-delimited file.

* Trans no
* Type
* Date
* Num
* Name
* Address 1
* Address 2
* Address 3
* Address 4
* Address 5
* Memo
* Item
* Account
* Clr
* Open Balance
* Qty
* VAT Code
* VAT Rate
* VAT Amount
* Amount

When importing, import your IIF Import File first, then go to Admin, Settings in this accounts package, and make sure everything is filled in, especially your financial year start - this is vital to ensure your VAT payments are matched correctly.

Finally, import your Transaction Detail Report.

Note that there is currently no way of importing payment history from Quick Books.

##Importing Quicken data

You can also import QIF files - but you should have transactions for all your accounts in a single file, because if you use a separate file for each account, transfers between accounts in different files will appear twice.

##Importing data from other packages

The system can import CSV or Tab delimited files from any system, provided they contain the correct fields. See the help link on the import screen for details of exactly which fields are required for each type of import.

##Every day running

The accounts package runs as a web server. While it is running, you can connect to it from any web server with access to your network (including phones and tablets which are on the same wireless network). The URL to connect is **http://localhost:8080/**, but with **localhost** replaced by the name or IP address of your computer. It is OK to leave the package running all day, and/or to add it to your startup group so it runs automatically when you log on. It could be run as a service (so it runs all the time your computer is switched on, even if you are not logged on), but this is not implemented yet (and would be different depending on whether you are running Linux or Windows).

If you do leave the package running all the time, it would be a good idea to create a bookmark to it in your web browser for ease of access.

Note that the Google Chrome browser gives the best user experience with this package. It can run in any browser, but most other browsers do support HTML5 as well (e.g. by offering drop-down calendars for dates).

##Backup and Restore

You should use the Backup option on the Admin menu regularly to backup your data. The backup is in standard JSON format. Note that Restore will overwrite all your data with the restored data, losing any changes made since you backed up.

##Skins and changing user interface style

You can now select "skins" to change the user interface style. Select the skin in Admin/Settings.

###Adding your own skins

You can add your own skins - to create a skin called **name**, just create 2 files, **name.css** and **name.js**,in the **html/skin** folder. You can enter any css you like in the css file to override the css in the regular **html/default.css** file. You can also add javascript in the js file (not recommended).

#Using more than 1 database

If you want to use more than 1 database (e.g. 1 for personal finances and one for company, or 1 for each person in your household), you can run multiple copies of AccountServer, using different configuration files for each one.

* Copy AccountServer.config in Windows Explorer:
  * Right click on AccountServer.config and choose "Copy" from the menu.
  * Right click on a blank space in the Windows Explorer menu, and choose "Paste" from the menu.
  * Right click on the new file (called something like "AccountServer - Copy.config"), and choose "Rename".
  * Type in a new name (e.g. **Personal.config**). The name **must** end with **.config**.
* Edit the new config file:
  * Change the ConnectionString setting to point to your new database. If the database is SQLite, all you need to change is the Data Source file name - e.g. **Personal.db"".
  * Change the Port setting to a different number (e.g. **8081**).
  * If running on Windows, register the new port:
     * Open a command prompt as administrator (in your start menu, search for "CMD", right click it, and choose "Run as Administrator").
     * In the command prompt, register the new port - type **netsh http add urlacl url=http://+:8081/ user=Everyone**
     * Close the command prompt.
* Now you can run another copy of AccountServer using the new config file - just create a shortcut to run "AccountServer Personal.config" (or whatever you called your config file).
* This copy will listen on the new port, and use the new database.
* You can connect to it by browsing to **http://localhost:8081/**

