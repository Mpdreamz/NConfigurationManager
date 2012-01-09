using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using NConfiguration.Website.Models;
using System.Configuration;

namespace NConfiguration.Website.Controllers
{
    public class HomeController : Controller
    {
        //
        // GET: /Home/

        public ActionResult Index()
        {
            var model = new HomeModel()
            {
                Environment = ConfigurationManager.AppSettings["environment"],
                Version = ConfigurationManager.AppSettings["version"]
            };
            return View(model);
        }

    }
}
