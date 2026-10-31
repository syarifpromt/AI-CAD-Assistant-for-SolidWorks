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
            if (args != null && args.Length > 0 && args[0] == "--live-test")
            {
                Console.WriteLine("=================================================");
                Console.WriteLine("  AI CAD Assistant - Live Gemini API Test        ");
                Console.WriteLine("=================================================");
                try
                {
                    var cred = new AICADAssistant.Core.Services.CredentialService();
                    var settings = AICADAssistant.Core.Models.AppSettings.Load();
                    var client = new AICADAssistant.Core.Gemini.GeminiClient(cred, settings);
                    Console.WriteLine("Active Model: " + client.CurrentModel);
                    Console.WriteLine("API Key Present: " + cred.HasApiKey());
                    var sw = Stopwatch.StartNew();
                    string res = client.SendChatAsync("Jelaskan perbedaan Extrude Boss, Extrude Cut, Revolve, dan Sweep.", null).Result;
                    sw.Stop();
                    Console.WriteLine("STATUS: SUCCESS");
                    Console.WriteLine("LATENCY: " + sw.ElapsedMilliseconds + " ms");
                    Console.WriteLine("RESPONSE PREVIEW:\n" + (res.Length > 500 ? res.Substring(0, 500) + "..." : res));
                    return 0;
                }
                catch (Exception ex)
                {
                    Exception actual = ex is AggregateException ? ex.InnerException : ex;
                    Console.WriteLine("STATUS: FAILED - " + actual.Message);
                    return 1;
                }
            }

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

                // Find TestInitialize and TestCleanup methods
                MethodInfo initMethod = null;
                MethodInfo cleanupMethod = null;
                foreach (var m in type.GetMethods())
                {
                    if (m.GetCustomAttribute<TestInitializeAttribute>() != null)
                    {
                        initMethod = m;
                    }
                    if (m.GetCustomAttribute<TestCleanupAttribute>() != null)
                    {
                        cleanupMethod = m;
                    }
                }

                foreach (var method in type.GetMethods())
                {
                    if (method.GetCustomAttribute<TestMethodAttribute>() == null)
                        continue;

                    totalTests++;
                    object instance = Activator.CreateInstance(type);
                    try
                    {
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
                    finally
                    {
                        if (cleanupMethod != null)
                        {
                            try
                            {
                                cleanupMethod.Invoke(instance, null);
                            }
                            catch { }
                        }
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

