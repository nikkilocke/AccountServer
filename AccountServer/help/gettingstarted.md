# Getting Started

The first thing is to go to *Admin* *Settings* and fill in all the details there. It is particularly important to get your year start correct - this should be set to your own financial year - if you want to use the UK government tax year, it starts on the 6th April, but only use this if you don't have your own accounting year.

For AccountServer to be able to send emails (e.g. invoices to your customers), you must fill in the details of your mail server - you should be able to get them from your ISP. The defaults are suitable for use with GMail (once you have entered your GMail user name and password).

You need to come back here to set your default bank account, once you have created your bank accounts.

There is a series of check boxes which configure which modules you wish to user. See the [Admin help](admin.md) for details.

If you want to import data from an existing system, do that next, otherwise set up your bank accounts, customers, suppliers and members as detailed below.

## Set up Users

If you want login security, set up your users. If there are no users on file, there is no login security. Once you add a user, all functionality will require logging in as a user. The first user you create will be the Admin user, who has access to everything. See the [Admin help](admin.md) for details.

## Importing data

If you wish to import data from Quick Books or Quicken, full details are provided [here](admin_import.md). You can also import from other packages if you can extract the data in Qif, CSV or tab-delimited format with the correct field names. See the help link under *Admin* *Import* for details of the field names required.

If you aren't importing a complete system, make sure you set up any data you aren't importing before you import something that depends on it - e.g. set up VAT codes before importing transactions with VAT on them, if you are VAT registered.

## Set up bank and credit card accounts

Go to *Banking* *New Account* to set up each account. Each account must have a unique name. Choose the type (Bank or Credit Card). The remaining fields are optional - Statement Format tells AccountServer how statements copied from the bank web page are laid out, so you can easily reconcile AccountServer with the bank, but you can leave that until you are ready to actually reconcile transactions. Next Cheque and Deposit numbers (from your cheque and deposit books) enable AccountServer to fill these details automatically, if required.

## Set up Account codes

Account codes are necessary to analyse income, expenditure, assets and liabilities, so you can produce a profit and loss account, balance sheet, etc.

The system comes with a minimal set of fixed account codes (such as Purchase Ledger, Sales Ledger, VAT Control, etc.). You need to set up other accounts to analyse income and expenditure and, for a business, assets and liabilities. Got to *Accounting* *New Account* to set up new accounts. Each account has a type, which determines where in the Profit and Loss account or Balance sheet it comes. You can optionally add a Code to each account, to determine the sort order within account type (otherwise accounts are sorted alphabetically by _Name_ within _Type_).

## Set up VAT codes, Products, Customers and Suppliers

If you are registered for VAT, you need to set up VAT codes in *Customers*/*VAT codes*. Click *New VAT code*, and enter the details. The code can be any unique code you like, and the rate is a percentage (e.g. 20 for 20%).

If you send out invoices to customers, you need to set up Products next. For a business selling stock, the products are the stock you sell, each with a unit price, VAT code (if VAT registered), and account code (when you sell a product, the sale is posted to that account code, so you can analyse sales by account code). For a business selling time by the hour or day, you need to set up a product code for each hourly or daily rate. Product codes can have a Unit, which can be decimal (e.g. for product sold by weight), days (for time sold by the day), hours (time sold by the hour), units (for items which are sold in whole numbers), or a specific number of decimal places.

If you send invoices to customers, you will want to add your customers next. Likewise, if you are invoiced by suppliers, set up your suppliers.

## Set up Members

If you are a membership organisation, you will want to enter your membership types and members. 

First set up the membership types, at least one for each annual subscription amount. If subscription payments are spread throughout the year, enter the number of payments (e.g. 12 for monthly payments).

Now you can add members. When you chose the membership type, the Annhual Subscription, Amount Due and Payment Amount will be filled in, though you can change these (e.g. if a member joins part way through the year, you may want to reduce the amount due accordingly).

## Set up Securities and Investments

If you hold securities and investments, you can set these up. 

A security is (e.g.) a share, unit trust, or other item which has a value which varies. Create securities with *Investments* *New Security* These have a name and ticker. Once created, you can set the current price for any security by opening it and pressing *New price*.

You buy and sell securities through an investment account (e.g. with a stockbroker). You can also use investment accounts for investments that are not securities (e.g. a pension). Set up investment accounts with *Investments* *New Account*.

