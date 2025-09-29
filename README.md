# OnTrack GraphQL Server

## Apollo Client configuration

Codex has refactored the front-end to use an Express/Apollo server that listens on port `4000`.  The React application still defaults to posting GraphQL requests to `http://localhost:8020/graphql`, which will fail because the checkout mutations (`setupSubscription`, `startSubscriptionCheckout`, and `completeSubscription`) are not registered on that endpoint.  To point the client at the new backend when running locally, set the `REACT_APP_BACKEND_GRAPHQL_URL` environment variable:

```bash
# .env
REACT_APP_BACKEND_GRAPHQL_URL=http://localhost:4000/graphql
```

If you do not set this variable before starting the React dev server, subscription checkout will continue to hit the deprecated `8020` endpoint and the app will display GraphQL schema errors.
