﻿using System;
using System.Windows.Forms;

namespace SoftUni_Course_Converter
{
    static class CourseConverterApp
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FormCourseConverter());
        }
    }
}
