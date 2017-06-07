using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AccountServer {
	public class Help : CodeFirstWebFramework.Help {

		public Importer[] Importers {
			get { return Importer.Importers; }
		}

		public void Admin_Import() {
			ReturnHelpFrom(Server.FileInfo("/help/admin_import.tmpl"));
		}

	}
}
