# AccountServer Accounting Software

## Full Documentation

For full documentation see [AccountServer/help/default.md](AccountServer/help/default.md)

## Installation

For installation, see [AccountServer/help/installation.md](AccountServer/help/installation.md)

## Importing Quick Books data

For importing, see [AccountServer/help/admin_import.md](AccountServer/help/admin_import.md)

## Every day running

The accounts package runs as a web server. While it is running, you can connect to it from any web server with access to your network (including phones and tablets which are on the same wireless network). The URL to connect is **http://localhost:8080/**, but with **localhost** replaced by the name or IP address of your computer. It is OK to leave the package running all day, and/or to add it to your startup group so it runs automatically when you log on. It could be run as a service (so it runs all the time your computer is switched on, even if you are not logged on), but this is not implemented yet (and would be different depending on whether you are running Linux or Windows).

If you do leave the package running all the time, it would be a good idea to create a bookmark to it in your web browser for ease of access.

Note that the Google Chrome browser gives the best user experience with this package. It can run in any browser, but most other browsers do not support HTML5 as well (e.g. by offering drop-down calendars for dates).

## Backup and Restore

You should use the Backup option on the Admin menu regularly to backup your data. The backup is in standard JSON format. Note that Restore will overwrite all your data with the restored data, losing any changes made since you backed up.

## Skins and changing user interface style

You can now select "skins" to change the user interface style. Select the skin in Admin/Settings.

### Adding your own skins

You can add your own skins - to create a skin called **name**, just create 2 files, **name.css** and **name.js**,in the **CodeFirstWebFramework/skin** folder. You can enter any css you like in the css file to override the css in the regular **AccountServer/default.css** file. You can also add javascript in the js file (not recommended).

# Using more than 1 database

If you want to use more than 1 database (e.g. 1 for personal finances and one for company, or 1 for each person in your household), AccountServer can use a different database depending on the url you use to access it. For example, your local computer can be accessed using `http://localhost:8080/` or `http://127.0.0.1:8080/`

You achieve this by adding entries to the Servers array in AccountServer.config, as follows:

	"Servers": [
		{
		"ServerName": "localhost",
		"Namespace": "AccountServer",
		"Title": "AccountServer Database",
		"Database": "SQLite",
		"ConnectionString": "Data Source=C:/ProgramData/AccountServer/AccountServer.db"
		},
		{
		"ServerName": "127.0.0.1",
		"Namespace": "AccountServer",
		"Title": "Personal Database",
		"Database": "SQLite",
		"ConnectionString": "Data Source=C:/ProgramData/AccountServer/Personal.db"
		}
	]

You can add more named urls by editing your hosts file (on Windows this is in C:\Windows\System32\drivers\etc\hosts), fort example by adding the following line:

	127.0.0.1 personal accounts

(You can add more different names separated by spaces on the same line if you needs more than 2 databases)

The servers array would then be:

	"Servers": [
		{
		"ServerName": "accounts",
		"Namespace": "AccountServer",
		"Title": "AccountServer Database",
		"Database": "SQLite",
		"ConnectionString": "Data Source=C:/ProgramData/AccountServer/AccountServer.db"
		},
		{
		"ServerName": "personal",
		"Namespace": "AccountServer",
		"Title": "Personal Database",
		"Database": "SQLite",
		"ConnectionString": "Data Source=C:/ProgramData/AccountServer/Personal.db"
		}
	]

and the urls would be http://accounts:8080/ and http://personal:8080/