# Importing Quick Books data

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

# Importing Quicken data

You can also import QIF files - but you should have transactions for all your accounts in a single file, because if you use a separate file for each account, transfers between accounts in different files will appear twice.

# Importing data from other packages

The system can import CSV or Tab delimited files from any system, provided they contain the correct fields.

