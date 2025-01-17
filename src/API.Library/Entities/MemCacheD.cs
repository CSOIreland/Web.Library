﻿using Enyim.Caching.Memcached;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Dynamic;
using System.Net;
using System.Text;
using System.Security.Cryptography;

namespace API
{
    /// <summary>
    /// Handling MemCacheD based on the Enyim client
    /// </summary>
    public class MemCacheD : ICacheD
    {
        #region Properties

        /// <summary>
        /// SubKey prefix
        /// </summary>
        internal static String SubKeyPrefix = "subKey_";

        /// <summary>
        /// Initiate MemCacheD
        /// </summary>

        #endregion

        public MemCacheD()
        {
            //test to see if memcache is working
            if (ApiServicesHelper.CacheConfig.API_MEMCACHED_ENABLED)
            {
                var port = ApiServicesHelper.Configuration.GetSection("enyimMemcached:Servers:0:Port").Value;
                var address = ApiServicesHelper.Configuration.GetSection("enyimMemcached:Servers:0:Address").Value;

                try
                {
                    ServerStats stats = ApiServicesHelper.MemcachedClient.Stats();            
                    var upTime = stats.GetUptime(new IPEndPoint(IPAddress.Parse(address), int.Parse(port)));
                }
                catch (Exception ex)
                {
                    Log.Instance.Fatal(ex);
                    Log.Instance.Fatal("Memcache has not returned any stats data. Memcache may be unavailable");
                }
            }
        }

        #region Methods
        /// <summary>
        /// Check if MemCacheD is enabled
        /// </summary>
        /// <returns></returns>
        private static bool IsEnabled()
        {
            /// <summary>
            /// Flag to indicate if MemCacheD is enabled 
            /// </summary>
            Log.Instance.Info("MemCacheD Enabled: " + ApiServicesHelper.CacheConfig.API_MEMCACHED_ENABLED);

            if (ApiServicesHelper.CacheConfig.API_MEMCACHED_ENABLED)
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        /// <summary>
        /// Check if MemCacheD cas is locked
        /// </summary>
        /// <returns></returns>
        private bool IsCasLocked(string repository)
        {
            if (string.IsNullOrEmpty(repository))
            {
                return false;
            }
            else
            {
                // Force to Case Insensitive
                repository = repository.ToUpper();
            }


            Log.Instance.Info("IsCasLocked repository is " + repository);

            MemCachedD_Value casLock = Get_BSO<dynamic>("API", "MemCacheD", "casManagement",  repository + "-Lock");


            if (casLock.hasData)
            {
                return casLock.data.Value;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Generate the Key for ADO
        /// </summary>
        /// <param name="nameSpace"></param>
        /// <param name="procedureName"></param>
        /// <param name="inputParams"></param>
        /// <returns></returns>
        private string GenerateKey_ADO(string nameSpace, string procedureName, List<ADO_inputParams> inputParams)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return null;
            }
            // Initiate
            string hashKey = "";

            try
            {
                // Initilise a new dynamic object
                dynamic keyObject = new ExpandoObject();
                // Implement the interface for handling dynamic properties
                var keyObject_IDictionary = keyObject as IDictionary<string, object>;

                // Add the reference to the nameSpace to the Object
                keyObject_IDictionary.Add("nameSpace", nameSpace);

                // Add the reference to the Procedure to the Object
                keyObject_IDictionary.Add("procedureName", procedureName);

                // Add the reference to the Params to the Object
                keyObject_IDictionary.Add("inputParams", Utility.JsonSerialize_IgnoreLoopingReference(inputParams));

                // Generate the composed Key
                hashKey = GenerateHash(keyObject_IDictionary);
                return hashKey;
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                return hashKey;
            }
        }

        /// <summary>
        /// Generate the Key for BSO
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <returns></returns>
        private string GenerateKey_BSO<T>(string nameSpace, string className, string methodName, T inputDTO)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return null;
            }

            // Initiate
            string hashKey = "";

            try
            {
                // Initilise a new dynamic object
                dynamic keyObject = new ExpandoObject();
                // Implement the interface for handling dynamic properties
                var keyObject_IDictionary = keyObject as IDictionary<string, object>;

                // Add the reference to the nameSpace to the Object
                keyObject_IDictionary.Add("nameSpace", nameSpace);

                // Add the reference to the className to the Object
                keyObject_IDictionary.Add("className", className);

                // Add the reference to the methodName to the Object
                keyObject_IDictionary.Add("methodName", methodName);

                // Add the reference to the inputDTO to the Object
                keyObject_IDictionary.Add("inputDTO", Utility.JsonSerialize_IgnoreLoopingReference(inputDTO));

                // Generate the composed Key
                hashKey = GenerateHash(keyObject_IDictionary);
                return hashKey;
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                return hashKey;
            }
        }

        /// <summary>
        /// Get a SubKey for a Key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static string GetSubKey(string key)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return null;
            }

            return SubKeyPrefix + key;
        }

        /// <summary>
        /// Check if a subKey exists
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static bool IsSubKey(dynamic data, string key)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return false;
            }
            // A key must be either a String or a JValue returned from deserialisation
            if (data.GetType() == typeof(String) || data.GetType() == typeof(JValue))
            {
                // Check the explicit casting to String against the subKey 
                if ((String)data == GetSubKey(key))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Generate the Hash
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private string GenerateHash(IDictionary<string, object> input)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return null;
            }
            // Initiate
            string hashKey = "";

            try
            {
                /// <summary>
                /// Salsa code to isolate the cache records form other applications or environments
                /// </summary>
                string API_MEMCACHED_SALSA = ApiServicesHelper.CacheConfig.API_MEMCACHED_SALSA;
                // Append the SALSA code
                input.Add("salsa", API_MEMCACHED_SALSA);

                hashKey = GetSHA256(Utility.JsonSerialize_IgnoreLoopingReference(input));
                return hashKey;
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                return hashKey;
            }
        }

        /// <summary>
        /// Store a record for ADO
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="procedureName"></param>
        /// <param name="inputParams"></param>
        /// <param name="data"></param>
        /// <param name="expiresAt"></param>
        /// <param name="validFor"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        private bool Store_ADO<T>(string nameSpace, string procedureName, List<ADO_inputParams> inputParams, dynamic data, DateTime expiresAt, TimeSpan validFor, string repository)
        {
            try
            {
                // Check if it's enabled first
                if (!IsEnabled())
                {
                    return false;
                }

                // Validate Expiry parameters
                if (!ValidateExpiry(ref expiresAt, ref validFor))
                {
                    return false;
                }

                // Get the Key
                string key = GenerateKey_ADO(nameSpace, procedureName, inputParams);

                // Get the Value
                MemCachedD_Value value = SetValue(data, expiresAt, validFor);

                // Store the Value by Key
                if (Store(key, value, validFor, repository))
                {
                    return true;
                }
                else
                {
                    string paramsSerialized = Utility.JsonSerialize_IgnoreLoopingReference(inputParams);
                    throw new Exception(String.Format($"Store_ADO: Cache store failed for namespace {0}, procedure {1}, parameters {2}", nameSpace, procedureName, paramsSerialized));

                }
            }
            catch (Exception ex)
            {
                Log.Instance.Error(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Store a record for ADO with an expiry date
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="procedureName"></param>
        /// <param name="inputParams"></param>
        /// <param name="data"></param>
        /// <param name="expiresAt"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        public bool Store_ADO<T>(string nameSpace, string procedureName, List<ADO_inputParams> inputParams, dynamic data, DateTime expiresAt, string repository = null)
        {
            return Store_ADO<T>(nameSpace, procedureName, inputParams, data, expiresAt, new TimeSpan(0), repository);
        }

        /// <summary>
        /// Store a record for ADO with a validity
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="procedureName"></param>
        /// <param name="inputParams"></param>
        /// <param name="data"></param>
        /// <param name="validFor"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        public bool Store_ADO<T>(string nameSpace, string procedureName, List<ADO_inputParams> inputParams, dynamic data, TimeSpan validFor, string repository = null)
        {
            return Store_ADO<T>(nameSpace, procedureName, inputParams, data, new DateTime(0), validFor, repository);
        }

        /// <summary>
        /// Store a record for BSO
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <param name="data"></param>
        /// <param name="expiresAt"></param>
        /// <param name="validFor"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        private bool Store_BSO<T>(string nameSpace, string className, string methodName, T inputDTO, dynamic data, DateTime expiresAt, TimeSpan validFor, string repository)
        {
            try
            {
                // Check if it's enabled first
                if (!IsEnabled())
                {
                    return false;
                }

                // Validate Expiry parameters
                if (!ValidateExpiry(ref expiresAt, ref validFor))
                {
                    return false;
                }

                // Get the Key
                string key = GenerateKey_BSO(nameSpace, className, methodName, inputDTO);

                // Get the Value
                MemCachedD_Value value = SetValue(data, expiresAt, validFor);

                // Store the Value by Key
                if (Store(key, value, validFor, repository))
                {
                    return true;
                }
                else
                {
                    string dtoSerialized = Utility.JsonSerialize_IgnoreLoopingReference(inputDTO);
                    throw new Exception(String.Format($"Memcache Store_ADO: Cache store failed for namespace {0}, className {1},methodName {2},dto {3}", nameSpace, className, methodName, dtoSerialized));

                }
            }
            catch (Exception ex)
            {
                Log.Instance.Error(ex.Message);
                return false;
            }
        }


        /// <summary>
        /// Store a record for BSO with a cache lock
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <param name="data"></param>
        /// <param name="expiresAt"></param>
        /// <param name="validFor"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        private bool Store_BSO_REMOVELOCK<T>(string nameSpace, string className, string methodName, T inputDTO, dynamic data, DateTime expiresAt, TimeSpan validFor, string repository)
        {
            try
            {

                // Check if it's enabled first
                if (!IsEnabled())
                {
                    return false;
                }

                //check if cachelock is enabled
                if (!ApiServicesHelper.CacheConfig.API_CACHE_LOCK_ENABLED)
                {
                    return false;
                }

                // Validate Expiry parameters
                if (!ValidateExpiry(ref expiresAt, ref validFor))
                {
                    return false;
                }

                // Get the Key
                string key = GenerateKey_BSO(nameSpace, className, methodName, inputDTO);

                // Get the Value
                MemCachedD_Value value = SetValue(data, expiresAt, validFor);

                // Store the Value by Key
                if (Store(key, value, validFor, repository))
                {
                    //Unlock the meta cache
                    string metaKey = GenerateKey_BSO(nameSpace + ApiServicesHelper.CacheConfig.API_CACHE_LOCK_PREFIX, className, methodName, inputDTO);
                    MemCachedD_Value mvalue = new();
                    mvalue.data = "false";

                    Log.Instance.Info("Remove Cache lock for correlation ID " + APIMiddleware.correlationID.Value);
                    //expire cache immediately
                    Store(metaKey, mvalue, TimeSpan.Zero, null);
                    return true;
                }
                else
                {
                    string dtoSerialized = Utility.JsonSerialize_IgnoreLoopingReference(inputDTO);
                    throw new Exception(String.Format($"Memcache Store_ADO: Cache store failed for namespace {0}, className {1},methodName {2},dto {3}", nameSpace, className, methodName, dtoSerialized));

                }              
            }
            catch (Exception ex)
            {
                Log.Instance.Error(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Store a record for BSO with a expiry date
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <param name="data"></param>
        /// <param name="expiresAt"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        public bool Store_BSO<T>(string nameSpace, string className, string methodName, T inputDTO, dynamic data, DateTime expiresAt, string repository = null)
        {
            return Store_BSO<T>(nameSpace, className, methodName, inputDTO, data, expiresAt, new TimeSpan(0), repository);
        }


        /// <summary>
        /// Stores a BSO record using the semaphore method to prevent cache stampede
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="semaConfig"></param>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <param name="data"></param>
        /// <param name="expiresAt"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        public bool Store_BSO_REMOVELOCK<T>(string nameSpace, string className, string methodName, T inputDTO, dynamic data, DateTime expiresAt, string repository = null)
        {
            return Store_BSO_REMOVELOCK<T>(nameSpace, className, methodName, inputDTO, data, expiresAt, TimeSpan.Zero, repository);
        }

        /// <summary>
        /// Store a record for BSO with a validity
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <param name="data"></param>
        /// <param name="validFor"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        public bool Store_BSO<T>(string nameSpace, string className, string methodName, T inputDTO, dynamic data, TimeSpan validFor, string repository = null)
        {
            return Store_BSO<T>(nameSpace, className, methodName, inputDTO, data, new DateTime(0), validFor, repository);
        }

        /// <summary>
        /// Store the value object by key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="validFor"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        private bool Store(string key, MemCachedD_Value value, TimeSpan validFor, string repository)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return false;
            }

            if (IsCasLocked(repository))
            {
                return false;
            }

            Stopwatch sw = Stopwatch.StartNew();
            int? cacheTraceCompressLength = null;
            DateTime traceStart = DateTime.Now;
            bool successTraceFlag = true;
            if (value.data != null)
            {
                // Check if data is of type String or JValue 
                if (value.data.GetType() == typeof(String) || value.data.GetType() == typeof(JValue))
                {

                    /// <summary>
                    /// Max size in MB before splitting a string record in sub-cache entries 
                    /// </summary>
                    uint API_MEMCACHED_MAX_SIZE = ApiServicesHelper.CacheConfig.API_MEMCACHED_MAX_SIZE;


                    // Cast data to String and check if oversized
                    string sData = (String)value.data;
                    if (sData.Length * sizeof(Char) > API_MEMCACHED_MAX_SIZE * 1024 * 1024)
                    {
                        // Get a subKey
                        string subKey = GetSubKey(key);

                        // SubStore the data by subKey
                        if (SubStore(subKey, sData, validFor, repository))
                        {
                            // Override data with the subKey to fish it out later
                            value.data = subKey;
                        }
                        else
                        {
                            successTraceFlag = false;
                            return false;
                        }
                    }
                }
            }
            try
            {
                // The value must be serialised
                string cacheSerialised = Utility.JsonSerialize_IgnoreLoopingReference(value);
                Log.Instance.Info("Cache Size Serialised (Byte): " + cacheSerialised.Length * sizeof(Char));
                // The value must be compressed
                string cacheCompressed = Utility.GZipCompress(cacheSerialised);
                Log.Instance.Info("Cache Size Compressed (Byte): " + cacheCompressed.Length * sizeof(Char));
                cacheTraceCompressLength = cacheSerialised.Length * sizeof(Char);

                bool isStored = false;

                // Fix the MemCacheD issue with bad Windows' compiled version: use validFor instead of expiresAt
                // validFor and expiresAt match each other
                isStored = ApiServicesHelper.MemcachedClient.Store(StoreMode.Set, key, cacheCompressed, validFor);

                // Store Value by Key
                if (isStored)
                {
                    Log.Instance.Info("Store succesfull: " + key);

                    // Add the cached record into a Repository
                    if (!String.IsNullOrEmpty(repository))
                    {
                        CasRepositoryStore(key, repository);
                    }

                    return true;
                }
                else
                {
                    Log.Instance.Fatal("Store failed: " + key);
                    successTraceFlag = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                successTraceFlag = false;
                return false;
            }
            finally
            {
                sw.Stop();
                var duration = Utility.StopWatchToSeconds(sw);

                JObject obj = new JObject
                {
                   new JProperty("key",key),
                   new JProperty("repository",repository)
                };

                //when cache object expires
                DateTime expires = DateTime.Now + validFor;
                var serializedObj = Utility.JsonSerialize_IgnoreLoopingReference(obj);
                Log.Instance.Info("Memcache Store Execution Time (s): " + duration + " Object:" + serializedObj);
                CacheTrace.PopulateCacheTrace(serializedObj, traceStart, Utility.StopWatchToSeconds(sw), "Store", successTraceFlag, cacheTraceCompressLength, expires);
            }
        }

        /// <summary>
        /// SubStore the data string by subKey
        /// </summary>
        /// <param name="subKey"></param>
        /// <param name="data"></param>
        /// <param name="validFor"></param>
        /// <param name="repository"></param>
        /// <returns></returns>
        private bool SubStore(string subKey, string data, TimeSpan validFor, string repository)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return false;
            }

            Stopwatch sw = Stopwatch.StartNew();
            int? cacheTraceCompressLength = null;
            DateTime traceStart = DateTime.Now;
            bool successTraceFlag = true;
            try
            {
                // The data is a string, no need to serialize
                Log.Instance.Info("SubCache Size Uncompressed (Byte): " + data.Length * sizeof(Char));

                // The data must be compressed
                string subCacheCompressed = Utility.GZipCompress(data);
                Log.Instance.Info("SubCache Size Compressed (Byte): " + subCacheCompressed.Length * sizeof(Char));
                cacheTraceCompressLength = subCacheCompressed.Length * sizeof(Char);
                bool isStored = false;

                // Fix the MemCacheD issue with bad Windows' compiled version: use validFor instead of expiresAt
                // validFor and expiresAt match each other
                isStored = ApiServicesHelper.MemcachedClient.Store(StoreMode.Set, subKey, subCacheCompressed, validFor);

                // Store Value by Key
                if (isStored)
                {
                    Log.Instance.Info("SubStore succesfull: " + subKey);

                    // Add the cached record into a Repository
                    if (!String.IsNullOrEmpty(repository))
                    {
                        CasRepositoryStore(subKey, repository);
                    }

                    return true;
                }
                else
                {
                    Log.Instance.Fatal("SubStore failed: " + subKey);
                    successTraceFlag = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                successTraceFlag = false;
                return false;
            }
            finally
            {
                sw.Stop();
                var duration = Utility.StopWatchToSeconds(sw);

                JObject obj = new JObject
                {
                   new JProperty("subKey",subKey),
                   new JProperty("repository",repository)
                };


                var serializedObj = Utility.JsonSerialize_IgnoreLoopingReference(obj);
                Log.Instance.Info("Memcache SubStore Execution Time (s): " + duration + " Object:" + serializedObj);
                CacheTrace.PopulateCacheTrace(serializedObj, traceStart, Utility.StopWatchToSeconds(sw), "SubStore", successTraceFlag, cacheTraceCompressLength, null);
            }
        }

        /// <summary>
        /// Add a Key into a Cas Repository
        /// N.B. Cas records DO NOT exipire, see Enyim inline documentation
        /// </summary>
        /// <param name="key"></param>
        /// <param name="repository"></param>
        private void CasRepositoryStore(string key, string repository)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return;
            }

            if (String.IsNullOrEmpty(repository))
            {
                return;
            }
            else
            {
                // Force to Case Insensitive
                repository = repository.ToUpper();
            }

            Stopwatch sw = Stopwatch.StartNew();
            int? cacheTraceCompressLength = null;
            bool successTraceFlag = true;
            DateTime traceStart = DateTime.Now;

            // Initiate Keys
            List<string> keys = new List<string>();

            try
            {
                // Initiate loop
                bool pending = true;
                do
                {
                   
                    Log.Instance.Info("CasRepositoryStore repository is " + repository);
                    // Get list of Keys by Cas per Repository
                    CasResult<List<string>> casCache = new();
                    try
                    {
                        casCache = ApiServicesHelper.MemcachedClient.GetWithCas<List<string>>(repository);
                    }
                    catch
                    {
                        // Silent catching (see https://github.com/cnblogs/EnyimMemcachedCore/issues/211)

                        // Store the time of exception in the Cache
                        ApiServicesHelper.MemcachedClient.Store(StoreMode.Set, "GET_WITH_CAS_TIME_OF_EXCEPTION", DateTime.Now.ToString());
                    }

                    // Check if Cas record exists
                    if (casCache.Result != null && casCache.Result.Count > 0)
                    {
                        keys = casCache.Result;
                    }

                    // Append Key
                    keys.Add(key);


                    if (casCache.Cas != 0)
                    {
                        // Try to "Compare And Swap" if the Cas identifier exists
                        CasResult<bool> casStore = ApiServicesHelper.MemcachedClient.Cas(StoreMode.Set, repository, keys, casCache.Cas);
                        pending = !casStore.Result;
                    }
                    else
                    {
                        // Create a new Cas record
                        CasResult<bool> casStore = ApiServicesHelper.MemcachedClient.Cas(StoreMode.Set, repository, keys);
                        pending = !casStore.Result;

                    }
                    
                } while (pending);

                Log.Instance.Info("Key [" + key + "] added to Repository [" + repository + "]");
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                successTraceFlag = false;
                return;
            }
            finally
            {
                sw.Stop();
                var duration = Utility.StopWatchToSeconds(sw);


                JObject obj = new JObject
                {
                   new JProperty("key",key),
                   new JProperty("repository",repository)
                };

                var serializedObj = Utility.JsonSerialize_IgnoreLoopingReference(obj);
                Log.Instance.Info("Memcache CasRepositoryStore Execution Time (s): " + duration + " Cas Repository:" + serializedObj);
                CacheTrace.PopulateCacheTrace(serializedObj, traceStart, Utility.StopWatchToSeconds(sw), "CasRepositoryStore", successTraceFlag, cacheTraceCompressLength, null);
            }
        }



        /// <summary>
        /// Remove all the cached records stored into a Cas Repository
        /// </summary>
        /// <param name="repository"></param>
        public bool CasRepositoryFlush(string repository)
        {

            Exception ex = null;
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return false;
            }


            if (string.IsNullOrEmpty(repository))
            {
                return false;
            }
            else
            {
                // Force to Case Insensitive
                repository = repository.ToUpper();
            }

            /*** cas lock management **/
            if (IsCasLocked(repository))
            {
                return false;
            }

            ApiServicesHelper.CacheD.Store_BSO<dynamic>("API", "MemCacheD", "casManagement", repository + "-Lock", true, DateTime.Now.AddMinutes(2), "METACAS");
           
            MemCachedD_Value casLock = Get_BSO<dynamic>("API", "MemCacheD", "casManagement", repository + "-Lock");

            //lock failed to set
            if (!casLock.hasData)
            {
                return false;
            }
            /*** end cas lock management **/

            Stopwatch sw = Stopwatch.StartNew();
            DateTime traceStart = DateTime.Now;
            bool successTraceFlag = true;

            // Initiate Keys
            List<string> keys = new List<string>();
            int keyCount = 0;
            try
            {
                Log.Instance.Info("CasRepositoryFlush repository is " + repository);

                CasResult<List<string>> casCache = new();
                //Get list of Keys by Cas per Repository
                try
                {
                    casCache = ApiServicesHelper.MemcachedClient.GetWithCas<List<string>>(repository);
                }
                catch
                {
                    // Silent catching (see https://github.com/cnblogs/EnyimMemcachedCore/issues/211)

                    // Store the time of exception in the Cache
                    ApiServicesHelper.MemcachedClient.Store(StoreMode.Set, "GET_WITH_CAS_TIME_OF_EXCEPTION", DateTime.Now.ToString());
                }
                // Check if Cas record exists
                if (casCache.Result != null && casCache.Result.Count > 0)
                {
                    // Get the list of Keys
                    keys = casCache.Result;
                    keyCount = keys.Count;
                }

                foreach (var key in keys)
                {
                    try
                    {

                        if (!ApiServicesHelper.MemcachedClient.Remove(key))
                        {
                            //This will happen if the CAS contains an expired/removed key
                            //Make sure that it has really been removed
                            if (ApiServicesHelper.MemcachedClient.TryGet(key, out casCache))
                                successTraceFlag = false;

                        }
                    }
                    catch (Exception e)
                    {
                        ex = e;
                        successTraceFlag = false;
                        Log.Instance.Error(e);
                        Log.Instance.Error("Failed to remove key " + key + " in repository " + repository);

                    }

                }


                if (successTraceFlag)
                {
                    var checkData = new CasResult<List<string>>();
                    try
                    {
                        checkData = ApiServicesHelper.MemcachedClient.GetWithCas<List<string>>(repository);

                    }
                    catch
                    {
                        // Silent catching (see https://github.com/cnblogs/EnyimMemcachedCore/issues/211)

                        // Store the time of exception in the Cache
                        ApiServicesHelper.MemcachedClient.Store(StoreMode.Set, "GET_WITH_CAS_TIME_OF_EXCEPTION", DateTime.Now.ToString());
                    }
                    if (checkData.Equals(null)) return true;
                    if (checkData.Result == null) return true;

                    //For the slight possibility that something may have sneaked into the CAS between the gathering of keys and their disposal
                    if (keyCount >= checkData.Result.Count)
                    {
                        ApiServicesHelper.MemcachedClient.Remove(repository);
                        return true;
                    }
                    else return false;
                }

                else throw new Exception("Error clearing CAS repository " + repository, ex);  //CAS flush was not reliable

            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                successTraceFlag = false;
                return false;

            }
            finally
            {
                //unlock the cas
                Remove_BSO<dynamic>("API", "MemCacheD", "casManagement", repository + "-Lock");

                sw.Stop();
                var duration = Utility.StopWatchToSeconds(sw);

                JObject obj = new JObject
                {
                    new JProperty("repository",repository)
                };

                var serializedObj = Utility.JsonSerialize_IgnoreLoopingReference(obj);


                Log.Instance.Info("Memcache CasRepositoryFlush Execution Time (s): " + duration + " Repository:" + serializedObj);
                CacheTrace.PopulateCacheTrace(serializedObj, traceStart, duration, "CasRepositoryFlush", successTraceFlag, null, null);
            }
        }

        /// <summary>
        /// Get the Value for ADO
        /// </summary>
        /// <param name="nameSpace"></param>
        /// <param name="procedureName"></param>
        /// <param name="inputParams"></param>
        /// <returns></returns>
        public MemCachedD_Value Get_ADO(string nameSpace, string procedureName, List<ADO_inputParams> inputParams)
        {
            // Get the Key
            string key = GenerateKey_ADO(nameSpace, procedureName, inputParams);

            // Get Value by Key
            return Get(key);
        }

        /// <summary>
        /// Get the Value for BSO
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <returns></returns>
        public MemCachedD_Value Get_BSO<T>(string nameSpace, string className, string methodName, T inputDTO)
        {
            // Get the Key
            string key = GenerateKey_BSO(nameSpace, className, methodName, inputDTO);

            // Get Value by Key
            return Get(key);
        }

        /// <summary>
        /// Gets a BSO value. Uses the semaphore method
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <returns></returns>
        public MemCachedD_Value Get_BSO_WITHLOCK<T>(string nameSpace, string className, string methodName, T inputDTO)
        {
            // Create a MemcachedD_Value object to be returned
            MemCachedD_Value returnValue = new MemCachedD_Value();

            // Check if it's enabled first
            if (!IsEnabled())
            {
                return returnValue;
            }

            //check if cachelock is enabled
            if (!ApiServicesHelper.CacheConfig.API_CACHE_LOCK_ENABLED)
            {
                return returnValue;
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                //tracing variables
                bool cacheLockUsed = false;
                decimal? cacheLockDuration = null;

                int? cacheTraceCompressLength = null;
                DateTime traceStart = DateTime.Now;
                bool successTraceFlag = true;
                DateTime? traceExpiresAt = null;
                      
                // Get the Key of what is being looked for
                string key = GenerateKey_BSO(nameSpace, className, methodName, inputDTO);

                //get meta cache key
                string metaKey = GenerateKey_BSO(nameSpace + ApiServicesHelper.CacheConfig.API_CACHE_LOCK_PREFIX, className, methodName, inputDTO);

                //Is there a meta cache key and is it true?
                MemCachedD_Value metaValue = Get(metaKey);      

                if (metaValue.hasData)
                {
                    //There's a metacache entry 
                    if (metaValue.data.ToString().Equals("true"))
                    {
                        cacheLockUsed = true;

                        Log.Instance.Info("Correlation ID : " + APIMiddleware.correlationID.Value + " is waiting for cache item");
                        Stopwatch swPoll = new();
                        //MetaCache is locked - wait until it's scheduled to go free
                        //poll the meta cache every "API_CACHE_LOCK_POLL_INTERVAL" seconds
                        TimeSpan ts = metaValue.expiresAt - DateTime.Now;
                        swPoll.Start();
                        while (ts.TotalSeconds > 0 && swPoll.ElapsedMilliseconds / 1000 <= ts.TotalSeconds)
                        {

                            metaValue = Get(metaKey);

                            //if it doesnt have data or total time elapsed then exit loop
                            if (!metaValue.hasData)
                            {
                                Log.Instance.Info("Cache lock loop has been exited as lock has been removed for correlation ID " + APIMiddleware.correlationID.Value);
                                //lock has been removed so try again
                                swPoll.Stop();

                                cacheLockDuration = Utility.StopWatchToSeconds(swPoll);
                                returnValue = Get(key, cacheLockDuration, cacheLockUsed);
                             

                                returnValue.cacheLockDuration = cacheLockDuration;
                                returnValue.cacheLockUsed = cacheLockUsed;
                                return returnValue;
                            }

                            //pause the loop
                            Thread.Sleep(ApiServicesHelper.CacheConfig.API_CACHE_LOCK_POLL_INTERVAL);
                        }

                        swPoll.Stop();
                        //if time elapsed
                        if (swPoll.ElapsedMilliseconds / 1000 >= ts.TotalSeconds)
                        {
                            Log.Instance.Info("Cache lock loop has been exited as time elapsed for correlation ID " + APIMiddleware.correlationID.Value);
                            //time has elapsed and lock has not beed removed 

                            cacheLockDuration = Utility.StopWatchToSeconds(swPoll);

                            //record cache trace

                            sw.Stop();
                            var duration = Utility.StopWatchToSeconds(sw);

                            JObject obj = new JObject
                                {
                                  new JProperty("key",key)
                                 };

                            var serializedObj = Utility.JsonSerialize_IgnoreLoopingReference(obj);

                            Log.Instance.Info("Memcache get Execution Time (s): " + duration + " Key : " + serializedObj);
                            CacheTrace.PopulateCacheTrace(serializedObj, traceStart, duration, "GET_WITH_LOCK", successTraceFlag, cacheTraceCompressLength, traceExpiresAt, cacheLockDuration, cacheLockUsed);

                            returnValue.cacheLockDuration = cacheLockDuration;
                            returnValue.cacheLockUsed = cacheLockUsed;

                            return returnValue;
                        }
                    }
                }
                else
                {

                    //Cache is not locked so check if key exists
                    returnValue = Get(key);

                    //if key has data then return
                    if (returnValue.hasData)
                    {
                        return returnValue;
                    }
                    else
                    {
                        //key does not have data so add a lock to the cache to alleviate cache stampede
                        metaCacheLock(metaKey);
                        return returnValue;
                    }
                }
            }
            finally
            {
                //tidyup 
                sw.Stop();
            }

            return returnValue;
        }


        /// <summary>
        /// lock the cache item
        /// </summary>
        /// <param name="metaKey"></param>
        /// <returns></returns>
        private void metaCacheLock(string metaKey)
        {
            //create a lock on the meta cache
            MemCachedD_Value mvalue = new();
            mvalue.data = "true";
            mvalue.hasData = true;
            //ApiServicesHelper.CacheConfig.API_CACHE_LOCK_MAX_TIME is in milliseconds - timespan is in 100 nanosecond ticks
            TimeSpan tstore = new TimeSpan(0, 0, ApiServicesHelper.CacheConfig.API_CACHE_LOCK_MAX_TIME);
            //The cache will be locked until it is unlocked, however a short max time is included for safety reasons
            mvalue.expiresAt = DateTime.Now.AddSeconds(ApiServicesHelper.CacheConfig.API_CACHE_LOCK_MAX_TIME);

            Log.Instance.Info("Add Cache lock for correlation ID " + APIMiddleware.correlationID.Value);
            Store(metaKey, mvalue, tstore, null);
        }


        /// <summary>
        /// Get the Value
        /// </summary>
        /// <typeparam name="MemCachedD_Value"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        private MemCachedD_Value Get(string key, decimal? cacheLockDuration = null, bool cacheLockUsed = false)
        {
            MemCachedD_Value value = new MemCachedD_Value();

            // Check if it's enabled first
            if (!IsEnabled())
            {
                return value;
            }

            //Tracing information
            Stopwatch sw = Stopwatch.StartNew();
            int? cacheTraceCompressLength = null;
            DateTime traceStart = DateTime.Now;
            bool successTraceFlag = true;
            DateTime? traceExpiresAt = null;

            try
            {

                // Get the compressed cache by Key
                string cacheCompressed = ApiServicesHelper.MemcachedClient.Get<string>(key);

                if (!string.IsNullOrEmpty(cacheCompressed))
                {
                    Log.Instance.Info("Cache found: " + key);
                    Log.Instance.Info("Cache Size Compressed (Byte): " + cacheCompressed.Length * sizeof(Char));

                    // Decompress the cache
                    string cacheSerialised = Utility.GZipDecompress(cacheCompressed);
                    Log.Instance.Info("Cache Size Serialised (Byte): " + cacheSerialised.Length * sizeof(Char));
                    cacheTraceCompressLength = cacheSerialised.Length * sizeof(Char);

                    // The value must be deserialised
                    dynamic cache = Utility.JsonDeserialize_IgnoreLoopingReference(cacheSerialised);

                    DateTime cacheDateTime = (DateTime)cache.datetime;
                    DateTime cacheExpiresAt = (DateTime)cache.expiresAt;
                    TimeSpan cacheValidFor = (TimeSpan)cache.validFor;

                    traceExpiresAt = cacheExpiresAt;

                    Log.Instance.Info("Cache Date: " + cacheDateTime.ToString());
                    Log.Instance.Info("Cache Expires At: " + cacheExpiresAt.ToString());
                    Log.Instance.Info("Cache Valid For (s): " + cacheValidFor.TotalSeconds.ToString());
                    Log.Instance.Info("Cache Has Data: " + cache.hasData.ToString());

                    // Init subKey
                    string subKey = GetSubKey(key);
                    // Init isSubKey
                    bool isSubKey = IsSubKey(cache.data, key);

                    // double check the cache record is still valid if not cleared by the garbage collector
                    if (cacheExpiresAt > DateTime.Now || cacheDateTime.AddSeconds(cacheValidFor.TotalSeconds) > DateTime.Now)
                    {
                        // Set properties
                        value.datetime = cacheDateTime;
                        value.expiresAt = cacheExpiresAt;
                        value.validFor = cacheValidFor;
                        value.hasData = Convert.ToBoolean(cache.hasData);
                        value.data = isSubKey ? null : cache.data;

                        // Check for data in the subKey
                        if (isSubKey)
                        {
                            // Get subCacheCompressed from the subKey
                            string subCacheCompressed = ApiServicesHelper.MemcachedClient.Get<string>(subKey);

                            if (!String.IsNullOrEmpty(subCacheCompressed))
                            {
                                Log.Instance.Info("SubCache found: " + subKey);
                                Log.Instance.Info("SubCache Size Compressed (Byte): " + subCacheCompressed.Length * sizeof(Char));

                                // Decompress the cache
                                string subCache = Utility.GZipDecompress(subCacheCompressed);
                                Log.Instance.Info("SubCache Size Decompressed (Byte): " + subCache.Length * sizeof(Char));
                                value.data = subCache;
                            }
                            else
                            {
                                successTraceFlag = false;
                                Log.Instance.Info("SubCache not found: " + key);
                            }
                        }
                        return value;
                    }
                    else
                    {
                        Log.Instance.Info("Forced removal of expired cache");
                        // Remove the expired record
                        Remove(key);
                        successTraceFlag = false;
                    }
                }
                else
                {
                    successTraceFlag = false;
                    Log.Instance.Info("Cache not found: " + key);
                }
            }
            catch (Exception e)
            {
                successTraceFlag = false;
                Log.Instance.Fatal(e);
            }
            finally
            {
                sw.Stop();
                var duration = Utility.StopWatchToSeconds(sw);

                JObject obj = new JObject
                {
                   new JProperty("key",key)
                };

                var serializedObj = Utility.JsonSerialize_IgnoreLoopingReference(obj);

                Log.Instance.Info("Memcache get Execution Time (s): " + duration + " Key : " + serializedObj);
                CacheTrace.PopulateCacheTrace(serializedObj, traceStart, duration, "GET", successTraceFlag, cacheTraceCompressLength, traceExpiresAt, cacheLockDuration,cacheLockUsed);
            }

            return value;
        }

        /// <summary>
        /// Remove the record for ADO
        /// </summary
        /// <param name="nameSpace"></param>
        /// <param name="procedureName"></param>
        /// <param name="inputParams"></param>
        /// <returns></returns>
        public bool Remove_ADO(string nameSpace, string procedureName, List<ADO_inputParams> inputParams)
        {
            // Get the Key
            string key = GenerateKey_ADO(nameSpace, procedureName, inputParams);

            return Remove(key);
        }

        /// <summary>
        /// Remove the record for BSO
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="nameSpace"></param>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="inputDTO"></param>
        /// <returns></returns>
        public bool Remove_BSO<T>(string nameSpace, string className, string methodName, T inputDTO)
        {
            // Get the Key
            string key = GenerateKey_BSO(nameSpace, className, methodName, inputDTO);

            return Remove(key);
        }

        /// <summary>
        /// Remove the record by key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private bool Remove(string key)
        {
            // Chekc if it's enabled first
            if (!IsEnabled())
            {
                return false;
            }

            Stopwatch sw = Stopwatch.StartNew();
            int? cacheTraceCompressLength = null;
            DateTime traceStart = DateTime.Now;
            bool successTraceFlag = true;
            try
            {
                string subKey = GetSubKey(key);

                // Remove the (optional) subKey
                if (ApiServicesHelper.MemcachedClient.Remove(subKey))
                {
                    Log.Instance.Info("SubRemoval successful: " + subKey);
                }

                // Remove the Key
                if (ApiServicesHelper.MemcachedClient.Remove(key))
                {
                    Log.Instance.Info("Removal successful: " + key);
                    return true;
                }
                else
                {
                    Log.Instance.Info("Removal failed: " + key);
                    successTraceFlag = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                successTraceFlag = false;
                return false;
            }
            finally
            {
                sw.Stop();
                var duration = Utility.StopWatchToSeconds(sw);

                JObject obj = new JObject
                {
                   new JProperty("key",key),
                };

                var serializedObj = Utility.JsonSerialize_IgnoreLoopingReference(obj);


                Log.Instance.Info("Memcache remove Execution Time (s): " + duration + " Key : " + serializedObj);
                CacheTrace.PopulateCacheTrace(serializedObj, traceStart, duration, "REMOVE", successTraceFlag, cacheTraceCompressLength, null);
            }
        }

        /// <summary>
        /// Flushing the cache will clear the cache for ALL applications that use the memcache instance
        /// </summary>
        public void FlushAll()
        {
            // Check if it's enabled first
            if (IsEnabled())
            {
                Stopwatch sw = Stopwatch.StartNew();
                int? cacheTraceCompressLength = null;
                DateTime traceStart = DateTime.Now;
                bool successTraceFlag = true;
                try
                {
                    // Remove all records
                    ApiServicesHelper.MemcachedClient.FlushAll();

                    Log.Instance.Info("Flush completed");
                }
                catch (Exception e)
                {
                    successTraceFlag = false;
                    Log.Instance.Fatal(e);
                }
                finally
                {
                    sw.Stop();
                    var duration = Utility.StopWatchToSeconds(sw);
                    Log.Instance.Info("Memcache flush all Execution Time (s): " + duration);
                    CacheTrace.PopulateCacheTrace(null, traceStart, duration, "FLUSH", successTraceFlag, cacheTraceCompressLength, null);
                }
            }

        }

        /// <summary>
        /// Get server stats
        /// </summary>
        /// <returns></returns>
        public ServerStats GetStats()
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return null;
            }
            Stopwatch sw = Stopwatch.StartNew();
            bool successTraceFlag = true;
            DateTime traceStart = DateTime.Now;

            try
            {
                return ApiServicesHelper.MemcachedClient.Stats();
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                successTraceFlag = false;
                return null;
            }
            finally
            {
                sw.Stop();
                var duration = Utility.StopWatchToSeconds(sw);
                Log.Instance.Info("Memcache get stats Execution Time (s): " + duration);
                CacheTrace.PopulateCacheTrace(null, traceStart, duration, "GETSTATS", successTraceFlag, null, null);
            }

        }

        /// <summary>
        /// Set the Value object
        /// </summary>
        /// <param name="data"></param>
        /// <param name="expiresAt"></param>
        /// <param name="validFor"></param>
        /// <returns></returns>
        private static MemCachedD_Value SetValue(dynamic data, DateTime expiresAt, TimeSpan validFor)
        {

            MemCachedD_Value value = new MemCachedD_Value();

            // Check if it's enabled first
            if (!IsEnabled())
            {
                return value;
            }

            try
            {
                value.datetime = DateTime.Now;
                value.expiresAt = expiresAt;
                value.validFor = validFor;
                value.hasData = true;
                value.data = data;

                return value;
            }
            catch (Exception e)
            {
                Log.Instance.Fatal(e);
                return value;
            }
        }

        /// <summary>
        /// Validate the Expiry parameters
        /// </summary>
        /// <param name="expiresAt"></param>
        /// <param name="validFor"></param>
        private static bool ValidateExpiry(ref DateTime expiresAt, ref TimeSpan validFor)
        {
            // Check if it's enabled first
            if (!IsEnabled())
            {
                return false;
            }

            /// <summary>
            /// Maximum validity in number of seconds that MemCacheD can handle (30 days = 2592000)
            /// </summary>
            uint API_MEMCACHED_MAX_VALIDITY = ApiServicesHelper.CacheConfig.API_MEMCACHED_MAX_VALIDITY;

            /// <summary>
            /// Max TimeSpan
            /// </summary>
            TimeSpan maxTimeSpan = new TimeSpan(API_MEMCACHED_MAX_VALIDITY * TimeSpan.TicksPerSecond);

            if (expiresAt == new DateTime(0))
            {
                // Set Max if expiresAt is Default DateTime
                expiresAt = DateTime.Now.Add(maxTimeSpan);
            }
            if (validFor.TotalSeconds == 0)
            {
                // Set Max if validFor is 0
                validFor = maxTimeSpan;
            }

            // Cache cannot be in the past
            if (expiresAt < DateTime.Now || validFor.Ticks < 0)
            {
                Log.Instance.Error("WARNING: Cache Validity cannot be in the past");
                return false;
            }

            if (expiresAt > DateTime.Now.Add(maxTimeSpan))
            {
                // Override the TimeSpan with the max validity if it exceeds the max length allowed by MemCacheD
                Log.Instance.Error("WARNING: Cache Validity reduced to max 30 days (2592000 sec)");
                expiresAt = DateTime.Now.Add(maxTimeSpan);
            }

            if (validFor > maxTimeSpan)
            {
                // Override the TimeSpan with the max validity if it exceeds the max length allowed by MemCacheD
                Log.Instance.Error("WARNING: Cache Validity reduced to max 30 days (2592000 sec)");
                validFor = maxTimeSpan;
            }

            // Get the minimum between expiresAt and NOW + validFor
            if (expiresAt < DateTime.Now.Add(validFor))
            {
                // Calculate the expiry time from now 
                validFor = expiresAt.Subtract(DateTime.Now);
            }
            else
            {
                // Calculate the datetime to expires at from now
                expiresAt = DateTime.Now.Add(validFor);
            }

            return true;
            #endregion
        }

        /// <summary>
        /// GetSHA256 Hashing function for memcache (efficient)
        /// </summary>
        /// <param name="input"></param>
        private static string GetSHA256(string input)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }

                string hashSHA256 = builder.ToString();

                return hashSHA256;
            }
        }

    }

    /// <summary>
    /// Define the Value object
    /// </summary>
    public class MemCachedD_Value
    {
        #region Properties
        /// <summary>
        /// Timestamp of the cache
        /// </summary>
        public DateTime datetime { get; set; }

        /// <summary>
        /// Expiry date
        /// </summary>
        public DateTime expiresAt { get; set; }

        /// <summary>
        /// Validity
        /// </summary>
        public TimeSpan validFor { get; set; }

        /// <summary>
        /// Flag to indicate if the cache has data
        /// </summary>
        public bool hasData { get; set; }

        /// <summary>
        /// Cached data
        /// </summary>
        public dynamic data { get; set; }


        /// <summary>
        /// cacheLockUsed to determine if cache lock was used or not
        /// </summary>
        public bool cacheLockUsed = false;


        /// <summary>
        /// cacheLockDuration to indicate how long lock was in place for
        /// </summary>
        public decimal? cacheLockDuration;

        #endregion

        /// <summary>
        /// Cache output
        /// </summary>
        public MemCachedD_Value()
        {
            // Set default
            datetime = DateTime.Now;
            expiresAt = new DateTime(0);
            validFor = new TimeSpan(0);
            hasData = false;
            data = null;
            cacheLockUsed = false;
            cacheLockDuration = null;
        }
    }

}
