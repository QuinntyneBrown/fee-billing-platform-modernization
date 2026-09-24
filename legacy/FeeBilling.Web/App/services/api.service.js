(function () {
    'use strict';

    angular.module('feeBilling')
        .factory('api', api);

    api.$inject = ['$http'];

    function api($http) {
        // Relative on purpose so it works under an IIS virtual directory. Only works if the shell
        // is served from the site root (/), not /Home/Index.
        var baseUrl = 'api/';

        // TODO FB-279: POSTs don't send the anti-forgery token. Web API doesn't check it anyway.

        var service = {
            // billing
            getRuns: getRuns,
            getRun: getRun,
            startRun: startRun,
            getRunInvoices: getRunInvoices,
            getRunInvoicesCsvUrl: getRunInvoicesCsvUrl,
            approveRun: approveRun,
            getInvoicePdfUrl: getInvoicePdfUrl,

            // fee schedules
            getSchedules: getSchedules,
            getSchedule: getSchedule,
            createSchedule: createSchedule,
            updateSchedule: updateSchedule,

            // accounts / households
            getAccounts: getAccounts,
            getHousehold: getHousehold,
            getPositions: getPositions
        };

        return service;

        function unwrap(response) {
            return response.data;
        }

        function getRuns(firmId) {
            return $http.get(baseUrl + 'billing/runs', { params: { firmId: firmId } }).then(unwrap);
        }

        function getRun(runId) {
            return $http.get(baseUrl + 'billing/runs/' + runId).then(unwrap);
        }

        // Returns { runId: 123 } (camelCase - the server returns an anonymous object).
        function startRun(firmId, periodEnd) {
            return $http.post(baseUrl + 'billing/runs', { FirmId: firmId, PeriodEnd: periodEnd }).then(unwrap);
        }

        // Returns a serialized DataSet: { "Table": [ ...invoice rows... ] }
        function getRunInvoices(runId) {
            return $http.get(baseUrl + 'billing/runs/' + runId + '/invoices').then(unwrap);
        }

        function getRunInvoicesCsvUrl(runId) {
            return baseUrl + 'billing/runs/' + runId + '/invoices?format=csv';
        }

        // Returns { runId: 123, approved: 456 }
        function approveRun(runId) {
            return $http.post(baseUrl + 'billing/runs/' + runId + '/approve').then(unwrap);
        }

        function getInvoicePdfUrl(invoiceId) {
            return baseUrl + 'invoices/' + invoiceId + '/pdf';
        }

        function getSchedules() {
            return $http.get(baseUrl + 'feeschedules').then(unwrap);
        }

        function getSchedule(id) {
            return $http.get(baseUrl + 'feeschedules/' + id).then(unwrap);
        }

        function createSchedule(schedule) {
            return $http.post(baseUrl + 'feeschedules', schedule).then(unwrap);
        }

        function updateSchedule(id, schedule) {
            return $http.put(baseUrl + 'feeschedules/' + id, schedule).then(unwrap);
        }

        function getAccounts(firmId, search) {
            var params = { firmId: firmId };
            if (search) {
                params.search = search;
            }
            return $http.get(baseUrl + 'accounts', { params: params }).then(unwrap);
        }

        function getHousehold(id) {
            return $http.get(baseUrl + 'households/' + id).then(unwrap);
        }

        // asOf must be 'yyyy-MM-dd'
        function getPositions(accountId, asOf) {
            return $http.get(baseUrl + 'accounts/' + accountId + '/positions', { params: { asOf: asOf } }).then(unwrap);
        }
    }
})();
