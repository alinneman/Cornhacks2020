﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Mvc;
using CornhacksProject.Models;
using Newtonsoft.Json;

namespace CornhacksProject.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult References()
        {
            return View();
        }

        public async Task<AirVisualResult> GetAirVisualResultAsync(string city, string state)
        {
            //testing
            if(string.IsNullOrWhiteSpace(city))
                throw new Exception("city is a required field");
            city = "city=" + city;
            if (!string.IsNullOrWhiteSpace(state))
                state = "&state=" + state;

            using (var client = new HttpClient())
            {
                string address = $"https://api.airvisual.com/v2/city?{city}{state}&country=USA&key={Constants.airVisualApiKey}";
                string response = client.GetStringAsync(new Uri(address)).Result;
                var AVresult = JsonConvert.DeserializeObject<AirVisualResult>(response);
                return AVresult;
            }
        }

        public async Task<List<OpenChargeResult>> GetOpenChargeResultAsync(double latitude, double longitude, double distance = 20, string distanceUnit = "Miles")
        {
            using (var client = new HttpClient())
            {
                string address = $"https://api.openchargemap.io/v3/poi/?output=json&compact=true&verbose=false&countrycode=US&latitude={latitude}&longitude={longitude}&distance={distance}&distanceunit={distanceUnit}";
                string response = client.GetStringAsync(new Uri(address)).Result;
                var OCresult = JsonConvert.DeserializeObject<List<OpenChargeResult>>(response);
                return OCresult;
            }
        }

        public async Task<int> GetEnergyConsumptionsResultsAsync(string city, string state)
        {
            //testing
            if (string.IsNullOrWhiteSpace(city))
                throw new Exception("city is a required field");
            city = "city=" + city;
            if (!string.IsNullOrWhiteSpace(state))
            {
                string stateAbrev = "";
                if (Constants.states.ContainsKey(state.ToLower()))
                    stateAbrev = Constants.states[state.ToLower()];

                state = "&state_abbr=" + stateAbrev;
            }

            using (var client = new HttpClient())
            {
                string address = $"https://developer.nrel.gov/api/cleap/v1/energy_cohort_data?{city}{state}&api_key={Constants.energyConsumptionApiKey}";
                string response = client.GetStringAsync(new Uri(address)).Result;
                // non-standard result - include location name, can't standardize class the same way as others
                string s = response.Substring(response.IndexOf("\"table\""));
                s = "{" + s.Substring(0, s.Length-3);
                var ECResultTable = JsonConvert.DeserializeObject<EnergyConsumption>(s).table;
                return ECResultTable.residential_electric_use.avg;
            }
        }

        public async Task<CityPopulation> GetPopulationAsync(string city, string state)
        {
            if (string.IsNullOrWhiteSpace(city))
                throw new Exception("city is a required field");
            string query = "q=" + city;
            if (!string.IsNullOrWhiteSpace(state))
            {
                string stateAbrev = "";
                if (Constants.states.ContainsKey(state.ToLower()))
                    stateAbrev = Constants.states[state.ToLower()];
                
                query = query + " " + stateAbrev + "&";
            }

            using (var client = new HttpClient())
            {
                string addy = $"https://public.opendatasoft.com/api/records/1.0/search/?dataset=worldcitiespop&{query}rows=1&sort=population&facet=country&refine.country=us";
                var response = client.GetStringAsync(new Uri(addy)).Result;
                var PopResult = JsonConvert.DeserializeObject<CityPopulation>(response);
                return PopResult;
            }
        }

        public double SustainabilityScore(string city, string state)
        {
            // convert 2 letter state to full name for some requests
            // ex: given NE, select dictionary key for the first entry with value NE (key is full name)
            if (state.Length == 2)
                state = Constants.states.FirstOrDefault(s => s.Value == state).Key;

            // population data
            CityPopulation p = GetPopulationAsync(city, state).Result;
            int? population = p.records.FirstOrDefault()?.fields.population;
            if(population == null)
                throw new Exception("Population data error");

            // use coords from population data to find ev chargers
            // ev charger score (bonus points if > avg)
            List<double> coordinates = p.records[0].geometry.coordinates;
            var OCResultList = GetOpenChargeResultAsync(coordinates[1], coordinates[0]).Result;
            int numEVStations = OCResultList.Count(ocr => ocr.AddressInfo.Town == city);
            double EVStationScoreBonus = (double) numEVStations / population.Value;

            // energy consumption score
            int avgResidentialEnergyUse = GetEnergyConsumptionsResultsAsync(city, state).Result;
            double energyScore = (double) avgResidentialEnergyUse / population.Value;

            // air quality value
            var airResult = GetAirVisualResultAsync(city, state).Result;
            int airQuality = airResult.data.current.pollution.aqius;

            // air quality score based on us official air quality index rankings 
            int airScore;
            if (airQuality <= 50) airScore = 6;
            else if (airQuality <= 100) airScore = 5;
            else if (airQuality <= 200) airScore = 4;
            else if (airQuality <= 300) airScore = 3;
            else if (airQuality <= 400) airScore = 2;
            else if (airQuality <= 500) airScore = 1;
            else throw new Exception("api error: value not in expected range");

            int finalScore = 10;
            if (airScore < 6) finalScore -= (7 - airScore) / 2; // deductions for poor air quality (max of 3 removed)


            if (EVStationScoreBonus > .0002) finalScore++; // bonus point for ev chargers: approximate avg # evc / population = .2 / 1000 = .0002
            

            return finalScore;
        }
    }
}