# Auto-Forward Plugin for BTCPay Server

## Features
- Allows the creation of invoices for which the received funds will automatically be forwarded to another address.
- The target destination address is defined on invoice creation.
- The percentage of funds to be forwarded is defined on invoice creation.
- On creation, you can define if the fees should be subtracted from the amount or if you pay the fees.
- When an invoice is settled, a payout will be automatically created.
- If there are multiple payouts to the same destination, they will be bundled in 1 payout. In this case the old payout is cancelled and a new one is created.
- A payout processor can be used to automatically pay out the payouts every x hours (if you are using a hot wallet in your store).
- Cold wallets should also be supported, but you will need to manually sign for the payouts.
- Every time an invoice is settled, the calculation of the payouts is done again so there should be no discrepancies.
- An overview page exists where you can view the invoices to forward and their status.
- Before a payout is done, the destination needs to be pre-created and the destination must have a balance. This acts as a safeguard so the wrong destination can never receive too much.
- Only BTC OnChain payments are currently supported.

## API Calls

### Creating an invoice
```shell
curl -v --header "Authorization: token c980c2db7127c9a49729272bb3270ca3cae9be61" --data '{amount: 100, metadata: { autoForwardToAddress: "bcrt1q9q26gunpl7e0l45unnqqw9k3dzlsqeqlny3gpv", autoForwardPercentage: 0.99, autoForwardSubtractFeeFromAmount: true }}' --header "Content-Type: application/json" http://127.0.0.1:14142/api/v1/stores/DjrJznzYcnV2DFgy7uvM4yztzQugNwhMGEzfYZjXD86j/invoices
```

### Listing all known destinations for Auto-Forwarding
```shell
curl -v --header "Authorization: token c980c2db7127c9a49729272bb3270ca3cae9be61" --header "Content-Type: application/json" http://127.0.0.1:14142/api/v1/stores/DjrJznzYcnV2DFgy7uvM4yztzQugNwhMGEzfYZjXD86j/autoforward-destinations
```

### Creating a new destination for Auto-Forwarding
```shell
curl -v --header "Authorization: token c980c2db7127c9a49729272bb3270ca3cae9be61" --data '{destination: "bcrt1q9q26gunpl7e0l45unnqqw9k3dzlsqeqlny3gpv", paymentMethod: "BTC-OnChain" }' --header "Content-Type: application/json" http://127.0.0.1:14142/api/v1/stores/DjrJznzYcnV2DFgy7uvM4yztzQugNwhMGEzfYZjXD86j/autoforward-destinations
```

### Enabling payouts for a destination
```shell
curl -v -XPUT --header "Authorization: token c980c2db7127c9a49729272bb3270ca3cae9be61" --data '{payoutsAllowed: true }' --header "Content-Type: application/json" http://127.0.0.1:14142/api/v1/stores/DjrJznzYcnV2DFgy7uvM4yztzQugNwhMGEzfYZjXD86j/autoforward-destinations/053f71c3-4bc3-4f7e-bc4e-f1d976bf4c96
```


## Unit Tests
Since BTCPay Server Plugins don't support unit tests yet, the files in `Tests` should be symlinked to from the `BTCPayServer.Tests` project. We keep the actual files here so they stay with the plugin code, but they are never run here.