# Admin

## Settings

This allows you to set your name, address, phone, email, etc. details.

It is important to set the start of your financial year here - companies have a fixed financial year which normally 
starts on the first of the month. Individuals may prefer to have the financial year coincide with the government
tax year, which (in the UK) starts on the first Monday in April.

Once you have set up one or more bank accounts, you should set the default bank account here (which is filled in
as a default when creating transactions which will affect a bank account).

If you want the system to be able to send email (e.g. automatically emailing out customer invoices) you need
to set your ISP mail server details correctly here. You should be able to find the information on your mail
provider's web site.

It also shows the program and database version numbers, and allows you to select the UI skin. 

You can add new skins by creating a pair of css and js files in the skins folder - these are both loaded
after the standard css and js files, and may change the appearance however required.

## Users

If there are no users on file, there is no login security. Once you add a user, some or all functionality will 
require loging in as a user. The first user you create will be the Admin user, who has access to everything. 
You cannot delete this user until all the other users have been deleted (and deleting this user will turn off 
login security).

All other users have an Access Level, which can be set to None, Read Only, Read Write or Admin. All usage of the system 
requires one of these access levels.

You can also gain finer control over who has access to what by ticking Module Permissions. This will show a list of
all the modules and/or methods which require an access level, and you can set the user's level for each one individually.

## Integrity Check

This checks the integrity of all your data in the database, including:
* Every document balances
* Each customer and supplier's invoices, payments and amount outstanding balances
* Every document has all the journals required
* Member and name and address records match

## [Import](admin_import.md)

## Backup and Restore

Backup backs up your database to a json file, and downloads it so you can store it on your computer (or email it to someone).

Restore restores the database from a backup file - **this will overwrite all your data with the old data**.
