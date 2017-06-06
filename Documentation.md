# Using the AccountServer accounting system

For installation details see [README.md](README.md).

## Navigation

The top 3 lines of the page show the menus. The top line is the main menu, to select which module you want to use. The second line gives options in that module, and the third line shows options for the particualr screen in view.

On small devices such as mobile phones, the menu is hidden to save screen space - to display it, click the menu symbol ![Menu symbol](AccountServer/images/menu.png).

Input forms will show one or more of the following buttons

|Button|Function|
|------|--------|
|Save|Saves the data in the current form, but remains on the same page. If crteating a new invoice (say), clicking this will save the data and reveal new buttons to (e.g.) print the invoice.|
|Save and Close|Saves the data in the current form, and returns to the previous screen.|
|Save and New|Saves the data in the current form, and opens a new blank form to create another record.|
|Reset|Throws away any changes you have made to the current form. This will ask you to confirm before doing so.|
|Back|Closes the current form and returns to the previous screen.|
|Cancel|Throws away any changes you have made to the current form and returns to the previous screen. This will ask you to confirm before doing so.|

Many screens present a list of items - you can select an individual item from the screen by clicking on it (N.B. if you use a touch screen, touch an item once to move the highlight to it, then touch again to actually open it). If the number of items is too many to fit on the screen, they are presented a page at a time. You can navigate between the pages using the links at the bottom right of the page. You can also control how many items are shown on each page with the Show *N* entries drop down at the top left. There is a search bar at the top right - typing in this will instantly show just items which match what you have typed (you can clear the search with the x at the right end of the field). You can also sort the items by any of the columns by clicking on the column header - clicking sorts in ascending order, clicking again changes to a descending sort.

## Getting Started

The first thing is to go to *Admin Settings* and fill in all the details there. It is particularly important to get your year start correct - this should be set to your own financial year - the government tax year, it starts on the first Monday on April, but only use this if you don't have your own accounting year.

For AccountServer to be able to send emails (e.g. invoices to your customers), you must fill in the details of your mail server - you should be able to get them from your ISP. The defaults are suitable for use with GMail (once you have entered your GMail user name and password).

You need to come back here to set your default bank account, once you have created your bank accounts.

If you want to import data from an existing system, do that next, otherwise set up your bank accounts, customers, suppliers and members as detailed below.

### Importing data

If you wish to import data from Quick Books or Quicken, full details are provided in [README.md](README.md). You can also import from other packages if you can extract the data in Qif, CSV or tab-delimited format with the correct field names. See the help link under *Admin Import* for details.

If you aren't importing a complete system, make sure you set up any data you aren't importing before you import something that depends on it - e.g. set up VAT codes before importing transactions with VAT on them, if you are VAT registered.

### Set up bank and credit card accounts

Go to *Banking* *New Account* to set up each account. Each account must have a unique name. Choose the type (Bank or Credit Card). The remaining fields are optional - Statement Format tells AccountServer how statements copied from the bank web page are laid out, so you can easily reconcile AccountServer with the bank, but you can leave that until you are ready to actually reconcile transactions. Next Cheque and Deposit numbers (from your cheque and deposit books) enable AccountServer to fill these details automatically, if required.

### Set up Account codes

Account codes are necessary to analyse income, expenditure, assets and liabilities, so you can produce a profit and loss account, balance sheet, etc.

The system comes with a minimal set of fixed account codes (such as Purchase Ledger, Sales Ledger, VAT Control, etc.). You need to set up other accounts to analyse income and expenditure and, for a business, assets and liabilities. Got to *Accounting* *New Account* to set up new accounts. Each account has a type, which determines where in the Profit and Loss account or Balance sheet it comes. You can optionally add a Code to each account, to determine the sort order within accoun ttype (otherwise accounts are sorted alphabetically by Name within Type).

### Set up VAT codes, Products, Customers and Suppliers

If you are registered for VAT, you need to set up VAT codes in *Customers* *VAT codes*. Click *New VAT code*, and enter the details. The code can be any unique code you like, and the rate is a percentage (e.g. 20 for 20%).

If you send out invoices to customers, you need to set up Products next. For a business selling stock, the products are the stock you sell, each with a unit price, VAT code (if VAT registered), and account code (when you sell a product, the sale is posted to that account code, so you can analyse sales by account code). For a business selling time by the hour or day, you need to set up a product code for each hourly or daily rate. Product codes can have a Unit, which can be decimal (e.g. for product sold by weight), days (for time sold by the day), hours (time sold by the hour), units (for items which are sold in whole numbers), or a specific number of decimal places.

If you send invoices to customers, you will want to add your customers next. Likewise, if you are invoiced by suppliers, set up your suppliers.

### Set up Members

If you are a membership organisation, you will want to enter your membership types and members. 

First set up the membership types, at least one for each annual subscription amount. If subscription payments are spread throughout the year, enter the number of payments (e.g. 12 for monthly payments).

Now you can add members. When you chose the membership type, the Annhual Subscription, Amount Due and Payment Amount will be filled in, though you can change these (e.g. if a member joins paret way through the year, you may want to reduce the amount due accordingly).

### Set up Securities and Investments

If you hold securities and investments, you can set these up. 

A security is (e.g.) a share, unit trust, or other item which has a value which varies. Create securities with *Investments* *New Security* These have a name and ticker. Once created, you can set the current price for any security by opening it and pressing *New price*.

You buy and sell securities through an investment account (e.g. with a stockbroker). You can also use investment accounts for investments that are not securities (e.g. a pension). Set up investment accounts with *Investments* *New Account*.

## Home screen

### Summary

The Summary on the Home screen gives you a summary of your accounts. It contains your To do list for the next 7 days, then a summary of the balances in each of your bank accounts and credit cards, a list of unpaid invoices to customers, a list of unpaid supplier invoices, a list of the current values of all your investments, and finally your net worth.

To action a To do item, click on the checkbox next to it.

### To Do list

The To Do list contains memorised transactions (items which happen at regular intervals, which you have chosen to automate), and to do notes (which you can add with *Home* *New To Do*). 

You can amend or delete To Do items with *Home* *To Do* - this shows a list of all your To Do items. Click on one to select it. From here you can change the next date, repeat type and frequency, and the task description. If Repeat Type is Weekly, and Frequency is 4, the item will be repeated every 4 weeks. If the item is a memorised transaction, you can also click *Post Doc* to post the transaction to the database. If it is merely a reminder, click *Job Done* to mark it as complete.

## Customers

### Listing

The Customers Listing lists all customers who have an outstanding balance by default. You can change it to list all customers by clicking *Show All* (and change it back by clicking *Outstanding only*). Click on a customer to select it.

### Customer Detail

When you have clicked on a customer, you see the Customer Detail screen. This shows all outstanding invoices (and credit notes) for the customer. You can change it to list all invoices by clicking *Show All*. Click on an invoice or credit note to display it.

### Adding and amending Invoices and Credit Memos

### Payments

## Suppliers

### Listing

### Supplier Detail

### Bills & Credits

### Bill Payments

## Banking

### Listing

### Bank Detail

### Names

### Adding Transactions

### Importing statements

### Reconciling

## Investments

### Listing

### Adding transactions

### Securities

### Updating prices

### Adding accounts

## Accounting

### Listing

### Adding transactions

### VAT Return

## Reports

### Modifying a report

### Memorising a report

## Admin

### Integrity Check

### Import

### Backup and Restore

