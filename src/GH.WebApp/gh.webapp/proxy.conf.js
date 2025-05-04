const { log } = require("console");

module.exports = {
    "/api": {
      target:
        process.env["services__ghidentityserver__https__0"] ||
        process.env["services__ghidentityserver__http__0"],
      secure: process.env["NODE_ENV"] !== "development",
      pathRewrite: {
        "^/api": "",
      },
    },

    "/bff": {
      target:
        process.env["services__ghidentityserver__https__0"] ||
        process.env["services__ghidentityserver__http__0"],
      secure: process.env["NODE_ENV"] !== "development",
      logLevel: "debug",
      pathRewrite: {
        "^/bff": "",
      },
    },

    "/signin-oidc": {
      target:
        process.env["services__ghidentityserver__https__0"] ||
        process.env["services__ghidentityserver__http__0"],
      secure: process.env["NODE_ENV"] !== "development",
      pathRewrite: {
        "^/signin-oidc": "",
      },
    },
    "/signout-callback-oidc": {
      target:
        process.env["services__ghidentityserver__https__0"] ||
        process.env["services__ghidentityserver__http__0"],
      secure: process.env["NODE_ENV"] !== "development",
      pathRewrite: {
        "^/signout-callback-oidc": "",
      },
    },
    "/todos": {
      target:
        process.env["services__ghidentityserver__https__0"] ||
        process.env["services__ghidentityserver__http__0"],
      secure: process.env["NODE_ENV"] !== "development",
      pathRewrite: {
        "^/todos": "",
      },
    },
  };
  