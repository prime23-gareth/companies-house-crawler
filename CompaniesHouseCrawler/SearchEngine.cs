﻿// <copyright file="SearchEngine.cs" company="Prime 23 Consultancy Limited">
// Copyright © 2016-2020 Prime 23 Consultancy Limited. All rights reserved.</copyright>

using System;
using System.Collections.Generic;
using System.Linq;

using CompaniesHouseCrawler.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using RestSharp;
using RestSharp.Authenticators;
using RestSharp.Serialization.Json;

namespace CompaniesHouseCrawler
{
    public class SearchEngine
    {
        private const string BaseUrl = "https://api.company-information.service.gov.uk";
        private const string CompanyOfficerResource = "company/{0}/officers";
        private const string CompanyResource = "company/{0}";
        private const string OfficerResource = "search/officers";

        private readonly RestClient client;
        private readonly JsonDeserializer jsonDeserializer;
        private readonly ILogger<SearchEngine> logger;

        private int requestCount;

        public SearchEngine(ILogger<SearchEngine> logger, IConfiguration configuration)
        {
            this.logger = logger;

            string apiKey = configuration["ApiKey"];

            this.client = new RestClient(BaseUrl)
            {
                Authenticator = new HttpBasicAuthenticator(apiKey, string.Empty)
            };

            this.jsonDeserializer = new JsonDeserializer();
        }

        public void Execute(SearchModel search)
        {





            //TODO
            // Tidy and refactor this into re-usable/recursive code.
            // Start logging the output in a sensible format.
            // Validate results with CH.






            this.requestCount = 0;

            var officers = this.GetOfficers(search);
            if (officers == null)
            {
                // TODO
                return;
            }

            var appointmentsLinks = officers.Select(officer => officer.Links.Self);
            var appointments = this.GetAppointments(appointmentsLinks);
            if (appointments == null)
            {
                // TODO
                return;
            }

            var companyNumbers = appointments.Select(appointment => appointment.AppointedTo.CompanyNumber);
            var companies = this.GetCompanies(companyNumbers);
            if (companies == null)
            {
                // TODO
                return;
            }

            var companyOfficers = companies.SelectMany(company => company.Officers);

            var uniqueOfficers = new Dictionary<string, OfficerListItem>();
            foreach (OfficerListItem companyOfficer in companyOfficers)
            {
                uniqueOfficers.TryAdd(companyOfficer.Name, companyOfficer);
            }

            var uniqueOfficerAppointments = uniqueOfficers.Values.Select(link => link.Links.Officer.Appointments);
            appointments = this.GetAppointments(uniqueOfficerAppointments);

            this.logger.LogInformation("Request count = {0}", this.requestCount);
        }

        private bool ExecuteRequest(string resource, out IRestResponse response)
        {
            RestRequest request = new RestRequest(resource, Method.GET);
            return this.ExecuteRequest(request, out response);
        }

        private bool ExecuteRequest(IRestRequest request, out IRestResponse response)
        {
            response = this.client.Execute(request);
            this.requestCount++;
            return response.IsSuccessful;
        }

        private List<Appointment> GetAppointments(IEnumerable<string> appointmentsLinks)
        {
            var results = new List<Appointment>();

            foreach (string appointmentLink in appointmentsLinks)
            {
                if (!this.ExecuteRequest(appointmentLink, out IRestResponse response))
                {
                    continue;
                }

                AppointmentList appointmentList = this.jsonDeserializer.Deserialize<AppointmentList>(response);
                results.AddRange(appointmentList.Items);
            }

            return results;
        }

        private List<CompanyProfile> GetCompanies(IEnumerable<string> companyNumbers)
        {
            var results = new List<CompanyProfile>();

            foreach (string companyNumber in companyNumbers)
            {
                string resource = string.Format(CompanyResource, companyNumber);
                if (!this.ExecuteRequest(resource, out IRestResponse response))
                {
                    continue;
                }

                CompanyProfile profile = this.jsonDeserializer.Deserialize<CompanyProfile>(response);
                results.Add(profile);

                var officers = this.GetOfficers(companyNumber);
                profile.Officers.AddRange(officers);
            }

            return results;
        }

        private IEnumerable<OfficerListItem> GetOfficers(string companyNumber)
        {
            var results = new List<OfficerListItem>();

            string resource = string.Format(CompanyOfficerResource, companyNumber);

            if (this.ExecuteRequest(resource, out IRestResponse response))
            {
                OfficerList officerList = this.jsonDeserializer.Deserialize<OfficerList>(response);
                results.AddRange(officerList.Items);
            }

            return results;
        }

        private List<Officer> GetOfficers(SearchModel search)
        {
            IRestRequest request = new RestRequest(OfficerResource, Method.GET).AddParameter("q", search.Name);
            if (!this.ExecuteRequest(request, out IRestResponse response))
            {
                return null;
            }

            OfficerSearch officers = this.jsonDeserializer.Deserialize<OfficerSearch>(response);

            var results = new List<Officer>();

            var names = search.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (Officer officer in
                from officer in officers.Items
                let count = names.Count(t => officer.Title.Contains(t, StringComparison.OrdinalIgnoreCase))
                where count == names.Length
                select officer)
            {
                if (search.IsValidMonth &&
                    search.IsValidYear)
                {
                    if (officer.DateOfBirth.Month == search.Month &&
                        officer.DateOfBirth.Year == search.Year)
                    {
                        results.Add(officer);
                    }
                }
                else if (search.IsValidMonth)
                {
                    if (officer.DateOfBirth.Month == search.Month)
                    {
                        results.Add(officer);
                    }
                }
                else if (search.IsValidYear)
                {
                    if (officer.DateOfBirth.Year == search.Year)
                    {
                        results.Add(officer);
                    }
                }
                else
                {
                    results.Add(officer);
                }
            }

            return results;
        }
    }
}