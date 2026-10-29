using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICADAssistant.Tests
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  AI CAD Assistant - Automated Unit Test Suite   ");
            Console.WriteLine("=================================================");

            int totalTests = 0;
            int passedTests = 0;
            int failedTests = 0;
            var stopwatch = Stopwatch.StartNew();

            var assembly = Assembly.GetExecutingAssembly();
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<TestClassAttribute>() == null && !type.Name.EndsWith("Tests"))
                    continue;

                Console.WriteLine("\n[Class] " + type.Name);
                object instance = null;

                // Find TestInitialize method
                MethodInfo initMethod = null;
                foreach (var m in type.GetMethods())
                {
                    if (m.GetCustomAttribute<TestInitializeAttribute>() != null)
                    {
                        initMethod = m;
                        break;
                    }
                }

                foreach (var method in type.GetMethods())
                {
                    if (method.GetCustomAttribute<TestMethodAttribute>() == null)
                        continue;

                    totalTests++;
                    try
                    {
                        if (instance == null)
                        {
                            instance = Activator.CreateInstance(type);
                        }
                        if (initMethod != null)
                        {
                            initMethod.Invoke(instance, null);
                        }

                        var expectedEx = method.GetCustomAttribute<ExpectedExceptionAttribute>();
                        if (expectedEx != null)
                        {
                            bool caught = false;
                            try
                            {
                                method.Invoke(instance, null);
                            }
                            catch (TargetInvocationException ex)
                            {
                                if (expectedEx.ExceptionType.IsInstanceOfType(ex.InnerException))
                                {
                                    caught = true;
                                }
                                else
                                {
                                    throw ex.InnerException;
                                }
                            }

                            if (!caught)
                            {
                                throw new Exception("Expected exception " + expectedEx.ExceptionType.Name + " was not thrown.");
                            }
                        }
                        else
                        {
                            method.Invoke(instance, null);
                        }

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("  ✓ " + method.Name);
                        Console.ResetColor();
                        passedTests++;
                    }
                    catch (Exception ex)
                    {
                        Exception actualEx = ex is TargetInvocationException ? ex.InnerException : ex;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  ✗ " + method.Name + ": " + actualEx.Message);
                        Console.ResetColor();
                        failedTests++;
                    }
                }
            }

            stopwatch.Stop();
            Console.WriteLine("\n-------------------------------------------------");
            Console.WriteLine(string.Format("Total: {0} | Lolos: {1} | Gagal: {2} | Waktu: {3} ms", totalTests, passedTests, failedTests, stopwatch.ElapsedMilliseconds));
            Console.WriteLine("-------------------------------------------------");

            if (failedTests > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("HASIL PENGUJIAN: GAGAL");
                Console.ResetColor();
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("HASIL PENGUJIAN: SEMUA BERHASIL (PASSED)");
            Console.ResetColor();
            return 0;
        }
    }

    static class ReflectionExtensions
    {
        public static T GetCustomAttribute<T>(this MemberInfo member) where T : Attribute
        {
            var attrs = member.GetCustomAttributes(typeof(T), false);
            return attrs.Length > 0 ? (T)attrs[0] : null;
        }
    }
}
