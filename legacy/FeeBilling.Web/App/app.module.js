(function () {
    'use strict';

    angular.module('feeBilling', ['ngRoute'])
        .config(config);

    config.$inject = ['$locationProvider', '$httpProvider'];

    function config($locationProvider, $httpProvider) {
        // AngularJS 1.6 changed the default hash prefix from '' to '!'. Set it explicitly so the
        // #!/ links in Views/Home/Index.cshtml keep working whatever version is on the CDN. (FB-231)
        $locationProvider.hashPrefix('!');

        // IE11 caches Web API GET responses, so the billing run list never updated. (FB-219)
        if (!$httpProvider.defaults.headers.get) {
            $httpProvider.defaults.headers.get = {};
        }
        $httpProvider.defaults.headers.get['If-Modified-Since'] = 'Mon, 26 Jul 1997 05:00:00 GMT';
        $httpProvider.defaults.headers.get['Cache-Control'] = 'no-cache';
        $httpProvider.defaults.headers.get['Pragma'] = 'no-cache';
    }
})();
